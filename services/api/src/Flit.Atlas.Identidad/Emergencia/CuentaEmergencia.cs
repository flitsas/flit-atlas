using System.Buffers.Text;
using System.ComponentModel;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Emergencia;

/// <summary>Lo que imprime el comando: el enlace solo existe aquí (stdout del comando), nunca en un log ni en la base.</summary>
public sealed record EnlaceEmergencia(
    string Correo, string Url, DateTimeOffset VenceEn, bool CuentaCreada, IReadOnlyList<string> FactoresRetirados, int SesionesCerradas);

[Description("Cuenta de emergencia del enlace de activación.")]
public sealed record CuentaEmergenciaPublica(string Correo, string Nombre);

/// <summary>
/// Cuenta de administrador de emergencia (ATLAS-ADR-0027): una sola cuenta local de la organización interna, con
/// <c>atlas.superadmin</c> sin vencimiento (<see cref="OrigenAsignacion.Emergencia"/>), para el arranque de un ambiente y
/// para entrar si Entra ID no responde. No tiene contraseña por defecto: <see cref="ActivarAsync"/> (el comando de
/// consola) la crea si falta, le quita los factores, cierra sus sesiones y emite un enlace de un solo uso; con el enlace
/// se registra una passkey, o una contraseña con TOTP, igual que una cuenta externa (ATLAS-ADR-0025).
/// </summary>
public sealed class CuentaEmergencia(
    PlataformaDbContext plataforma,
    IdentidadDbContext identidad,
    UserManager<UsuarioAtlas> usuarios,
    FactoresExternos factores,
    AdministracionRoles roles,
    RegistroSeguridad registro,
    IInvalidadorSesiones invalidador,
    IConfiguration configuracion,
    IOptions<OpcionesEmergencia> opciones,
    TimeProvider reloj)
{
    /// <summary>Proveedor de la sesión (<c>proveedor</c> en <c>/yo</c>) cuando entra la cuenta de emergencia.</summary>
    public const string Proveedor = "emergencia";

    /// <summary>Nombre del proveedor de tokens de Identity (y de su protector de Data Protection).</summary>
    public const string ProveedorToken = "AtlasEmergencia";

    public const string Proposito = "atlas.emergencia.activacion";

    /// <summary>Ruta pública de la PWA que recibe el enlace (el token va en el fragmento).</summary>
    public const string RutaActivacion = "/emergencia";

    /// <summary>Código del 409 cuando la administración intenta desarmar la cuenta de emergencia.</summary>
    public const string CodigoProtegida = "cuenta_de_emergencia";

    public const string Severidad = "alta";

    private const string MensajeEnlace = "Este enlace ya se usó, venció o no es válido. Genera otro con el comando de emergencia.";

    public static string Ambito(Guid usuarioId) => $"emergencia:{usuarioId}";

    /// <summary>¿La persona es la cuenta de emergencia? La reconoce su asignación de emergencia, no el correo.</summary>
    public static Task<bool> EsDeEmergenciaAsync(PlataformaDbContext plataforma, Guid personaId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plataforma);
        return plataforma.AsignacionesRol.AnyAsync(a => a.PersonaId == personaId && a.Origen == OrigenAsignacion.Emergencia, ct);
    }

    /// <summary>
    /// Lo que hace el comando <c>emergencia activar</c>: crea la cuenta si no existe (idempotente), la reactiva, le quita
    /// todos los factores y el bloqueo, cierra sus sesiones (sello nuevo: el enlace anterior deja de servir) y emite un
    /// enlace de activación de un solo uso.
    /// </summary>
    public async Task<EnlaceEmergencia> ActivarAsync(CancellationToken ct = default)
    {
        var correo = (opciones.Value.Correo ?? string.Empty).Trim().ToLowerInvariant();
        var nombre = (opciones.Value.Nombre ?? string.Empty).Trim();
        if (correo.Length is < 3 or > 320 || !correo.Contains('@', StringComparison.Ordinal) || nombre.Length is 0 or > 200)
        {
            throw new CuentaEmergenciaException(
                $"Configuración inválida: {OpcionesEmergencia.Seccion}:Correo debe ser un correo y {OpcionesEmergencia.Seccion}:Nombre no puede quedar vacío.");
        }

        var (persona, usuario, creada, retirados) = await TransaccionCompartida.EjecutarAsync(plataforma, identidad, async () =>
        {
            // Dos comandos a la vez no crean dos cuentas: el candado vive hasta el final de la transacción.
            await plataforma.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('atlas.identidad.emergencia', 0))", ct);
            var ahora = reloj.GetUtcNow();
            var perfil = await plataforma.Personas.FirstOrDefaultAsync(
                p => plataforma.AsignacionesRol.Any(a => a.PersonaId == p.Id && a.Origen == OrigenAsignacion.Emergencia), ct);
            var esNueva = perfil is null;
            if (perfil is null)
            {
                if (await plataforma.Personas.AnyAsync(p => p.Correo == correo, ct))
                {
                    throw new CuentaEmergenciaException(
                        $"El correo {correo} ya es de otra persona de Atlas. Configura otro, ficticio, en {OpcionesEmergencia.Seccion}:Correo.");
                }
                perfil = new Persona
                {
                    Tipo = TipoPersona.Empleado,
                    OrganizacionId = (await OrganizacionInternaAsync(ct)).Id,
                    Nombre = nombre,
                    Correo = correo,
                };
                plataforma.Personas.Add(perfil);
                await plataforma.SaveChangesAsync(ct);
            }
            else if (perfil.Estado != EstadoPersona.Activa)
            {
                perfil.Estado = EstadoPersona.Activa;
                await plataforma.SaveChangesAsync(ct);
            }

            var cuenta = await usuarios.Users.FirstOrDefaultAsync(u => u.PersonaId == perfil.Id, ct);
            if (cuenta is null)
            {
                // Sin contraseña ni factor: hasta que se use el enlace, nadie puede entrar con ella.
                cuenta = new UsuarioAtlas { PersonaId = perfil.Id, UserName = perfil.Correo, Email = perfil.Correo, EmailConfirmed = true, CreadoEn = ahora };
                Exigir(await usuarios.CreateAsync(cuenta));
            }
            if (esNueva)
            {
                var asignada = await roles.AsignarAsync(perfil.Id, new PedidoAsignacion(RolesAtlas.Superadmin), null, OrigenAsignacion.Emergencia, ct);
                if (asignada.Error is { } error)
                {
                    throw new CuentaEmergenciaException($"No se pudo asignar {RolesAtlas.Superadmin}: {error.Mensaje}");
                }
            }

            var quitados = await factores.RetirarTodosAsync(cuenta);
            Exigir(await usuarios.SetLockoutEndDateAsync(cuenta, null));
            Exigir(await usuarios.ResetAccessFailedCountAsync(cuenta));

            var datos = Datos(cuenta, perfil);
            if (esNueva)
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.EmergenciaCuentaCreada, datos with
                {
                    Detalle = new { severidad = Severidad, origen = "consola", rol = RolesAtlas.Superadmin },
                }, ct);
            }
            if (quitados.Count > 0)
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.FactoresRetirados, datos with { Detalle = new { metodos = quitados, motivo = "emergencia" } }, ct);
            }
            return (true, (perfil!, cuenta!, esNueva, quitados));
        }, ct);

        // Sello nuevo y sesiones revocadas, después de guardar (ATLAS-ADR-0022): el enlace anterior ya no sirve.
        var cerradas = await invalidador.InvalidarSesionesAsync(persona.Id, MotivosInvalidacion.CambioDeCredenciales, null, ct);
        var vence = reloj.GetUtcNow() + opciones.Value.VigenciaEnlace;
        await registro.RegistrarAsync(TiposEventoSeguridad.EmergenciaEnlaceEmitido, Datos(usuario, persona) with
        {
            Detalle = new { severidad = Severidad, origen = "consola", cuenta_creada = creada, factores_retirados = retirados, sesiones_cerradas = cerradas, vence_en = vence },
        }, ct);

        // El token se arma al final, con el sello vigente, y solo se devuelve: nunca va a un log, a la auditoría ni a la base.
        var actualizado = await usuarios.FindByIdAsync(usuario.Id.ToString()) ?? usuario;
        var token = await usuarios.GenerateUserTokenAsync(actualizado, ProveedorToken, Proposito);
        var enlace = $"{actualizado.Id:N}.{Base64Url.EncodeToString(Convert.FromBase64String(token))}";
        return new EnlaceEmergencia(persona.Correo, $"{UrlPublica()}{RutaActivacion}#{enlace}", vence, creada, retirados, cerradas);
    }

    /// <summary>El usuario de emergencia del enlace, si el enlace aún sirve (mismo sello y dentro de su vigencia).</summary>
    public async Task<UsuarioAtlas?> UsuarioDelEnlaceAsync(string? enlace, CancellationToken ct = default)
    {
        var partes = (enlace ?? string.Empty).Split('.', 2);
        if (partes.Length != 2 || !Guid.TryParseExact(partes[0], "N", out var id) || partes[1].Length > 1024 || !Base64Url.IsValid(partes[1]))
        {
            return null;
        }
        var usuario = await usuarios.FindByIdAsync(id.ToString());
        if (usuario is null || !await EsDeEmergenciaAsync(plataforma, usuario.PersonaId, ct))
        {
            return null;
        }
        var token = Convert.ToBase64String(Base64Url.DecodeFromChars(partes[1]));
        return await usuarios.VerifyUserTokenAsync(usuario, ProveedorToken, Proposito, token) ? usuario : null;
    }

    /// <summary>Registra un intento con un enlace de emergencia que no sirve.</summary>
    public Task RechazarEnlaceAsync(CancellationToken ct) =>
        registro.RegistrarAsync(TiposEventoSeguridad.EnlaceRechazado, new DatosEventoSeguridad { Detalle = new { enlace = "emergencia" } }, ct);

    public async Task<CuentaEmergenciaPublica?> PublicaAsync(string? enlace, CancellationToken ct)
    {
        var usuario = await UsuarioDelEnlaceAsync(enlace, ct);
        if (usuario is null)
        {
            return null;
        }
        var nombre = await plataforma.Personas.Where(p => p.Id == usuario.PersonaId).Select(p => p.Nombre).SingleAsync(ct);
        return new CuentaEmergenciaPublica(usuario.Email!, nombre);
    }

    /// <summary>
    /// Activa la cuenta con el enlace: registra el factor (passkey, o contraseña con TOTP), genera los códigos de
    /// recuperación, cambia el sello (el enlace deja de servir) y abre la sesión. Como la recuperación de los externos, sin
    /// términos: la cuenta es interna.
    /// </summary>
    public async Task<ResultadoCuenta<CuentaLista>> CompletarActivacionAsync(
        string? enlace, SolicitudFactor? solicitud, HttpContext http, SesionExterna sesion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var usuario = await UsuarioDelEnlaceAsync(enlace, ct);
        if (usuario is null)
        {
            await RechazarEnlaceAsync(ct);
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status410Gone, CuentasExternas.EnlaceNoValido, MensajeEnlace);
        }
        if (solicitud is null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, "metodo_invalido", "Elige passkey o contraseña.");
        }
        var (factor, errorFactor) = await factores.PrepararAsync(solicitud, Ambito(usuario.Id), usuario, http);
        if (errorFactor is not null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, errorFactor.Codigo, errorFactor.Mensaje);
        }
        var persona = await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Id == usuario.PersonaId, ct);
        var datos = Datos(usuario, persona);

        var resultado = await TransaccionCompartida.EjecutarAsync(plataforma, identidad, async () =>
        {
            var retirados = await factores.RetirarTodosAsync(usuario);
            if (await factores.AplicarAsync(usuario, factor!) is { } error)
            {
                return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, error.Codigo, error.Mensaje));
            }
            var codigos = await factores.NuevosCodigosAsync(usuario);
            Exigir(await usuarios.SetLockoutEndDateAsync(usuario, null));
            Exigir(await usuarios.ResetAccessFailedCountAsync(usuario));
            // El sello nuevo invalida este mismo enlace (un solo uso).
            Exigir(await usuarios.UpdateSecurityStampAsync(usuario));
            if (retirados.Count > 0)
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.FactoresRetirados, datos with { Detalle = new { metodos = retirados, motivo = "emergencia" } }, ct);
            }
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = factor!.Metodo } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = "codigos_recuperacion", cantidad = codigos.Count } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.EmergenciaActivada, datos with
            {
                Detalle = new { severidad = Severidad, metodo = factor!.Metodo },
            }, ct);
            return (true, ResultadosCuenta.Ok<CuentaLista>(new CuentaLista(codigos, false)));
        }, ct);
        if (resultado.Error is not null)
        {
            return resultado;
        }
        await factores.OlvidarAsync(factor!);
        await invalidador.InvalidarSesionesAsync(persona.Id, MotivosInvalidacion.CambioDeCredenciales, null, ct);

        var actualizado = await usuarios.FindByIdAsync(usuario.Id.ToString());
        var abrir = await sesion.MotivoNoPermitidoAsync(persona.Id, ct) is null;
        if (abrir)
        {
            await sesion.IniciarAsync(http, actualizado!, factor!.Metodo, ct);
        }
        return ResultadosCuenta.Ok<CuentaLista>(resultado.Valor! with { SesionIniciada = abrir });
    }

    private static DatosEventoSeguridad Datos(UsuarioAtlas usuario, Persona persona) =>
        new() { UsuarioId = usuario.Id, PersonaId = persona.Id, OrganizacionId = persona.OrganizacionId };

    private async Task<Organizacion> OrganizacionInternaAsync(CancellationToken ct)
    {
        var interna = await plataforma.Organizaciones.FirstOrDefaultAsync(o => o.Tipo == TipoOrganizacion.Interna, ct);
        if (interna is not null)
        {
            return interna;
        }
        // Ambiente recién creado, sin ningún ingreso todavía: la misma organización que crea el primer ingreso de Entra.
        interna = new Organizacion { Nombre = AprovisionadorEmpleados.NombreOrganizacionInterna, Tipo = TipoOrganizacion.Interna };
        plataforma.Organizaciones.Add(interna);
        await plataforma.SaveChangesAsync(ct);
        return interna;
    }

    /// <summary>URL pública del ambiente (<c>Atlas:UrlPublica</c>, obligatoria en qa y pdn); en local, la de Vite.</summary>
    private string UrlPublica()
    {
        var configurada = configuracion[OpcionesExternos.ClaveUrlPublica];
        return string.IsNullOrWhiteSpace(configurada) ? "https://localhost:4032" : configurada.TrimEnd('/');
    }

    private static void Exigir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException("Identity rechazó el cambio: " + string.Join("; ", resultado.Errors.Select(e => e.Code)));
        }
    }
}
