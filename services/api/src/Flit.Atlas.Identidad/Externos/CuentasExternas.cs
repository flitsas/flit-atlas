using System.Buffers.Text;
using System.ComponentModel;
using System.Text.Json;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Externos;

[Description("Lo que ve el invitado antes de aceptar: a qué organización y con qué rol, y los documentos que debe aceptar.")]
public sealed record InvitacionPublica(
    [property: Description("Correo invitado: será el de la cuenta.")] string Correo,
    string? Nombre,
    string Organizacion,
    string Rol,
    string RolNombre,
    DateTimeOffset? AccesoVenceEn,
    DateTimeOffset EnlaceVenceEn,
    IReadOnlyList<DocumentoVigente> Documentos);

[Description("Aceptación de la invitación: nombre, versiones de los documentos aceptados y el factor de la cuenta.")]
public sealed record SolicitudAceptacion(
    [property: Description("Token del enlace (el fragmento de la URL del correo).")] string Token,
    [property: Description("Nombre completo del invitado.")] string Nombre,
    [property: Description("Versiones vigentes de los términos y del aviso de privacidad.")] IReadOnlyList<AceptacionDocumento> Documentos,
    SolicitudFactor Factor);

[Description("Cuenta creada o recuperada: los códigos de recuperación se muestran esta única vez.")]
public sealed record CuentaLista(
    [property: Description("Diez códigos de recuperación de un solo uso.")] IReadOnlyList<string> CodigosRecuperacion,
    [property: Description("true si ya quedó la sesión abierta.")] bool SesionIniciada);

/// <summary>Resultado de las operaciones de cuentas externas: valor, o error con su estado HTTP.</summary>
public sealed record ResultadoCuenta<T>(T? Valor, int Estado, ErrorExternos? Error);

public static class ResultadosCuenta
{
    public static ResultadoCuenta<T> Ok<T>(T valor) => new(valor, StatusCodes.Status200OK, null);

    public static ResultadoCuenta<T> Falla<T>(int estado, string codigo, string mensaje) => new(default, estado, new ErrorExternos(codigo, mensaje));
}

/// <summary>
/// Creación, recuperación y restablecimiento de las cuentas locales de externos (§6.2, HU 1.5, ATLAS-ADR-0025). Cada
/// operación corre en una transacción compartida por la plataforma y la identidad (<see cref="TransaccionCompartida"/>):
/// si el código TOTP no coincide o una asignación no se puede crear, no queda nada a medias.
/// </summary>
public sealed class CuentasExternas(
    PlataformaDbContext plataforma,
    IdentidadDbContext identidad,
    UserManager<UsuarioAtlas> usuarios,
    FactoresExternos factores,
    AdministracionRoles roles,
    DocumentosLegales legales,
    RegistroSeguridad registro,
    IInvalidadorSesiones invalidador,
    CorreosExternos correos,
    ColaCorreosIdentidad colaCorreos,
    IOptions<OpcionesExternos> opciones,
    TimeProvider reloj)
{
    public const string PropositoRecuperacion = "atlas.recuperacion";

    public const string EnlaceNoValido = "enlace_no_valido";
    private const string MensajeEnlace = "Este enlace ya se usó, venció o no es válido. Pide a quien te invitó que te lo reenvíe.";

    public static string AmbitoInvitacion(Guid id) => $"invitacion:{id}";

    public static string AmbitoRecuperacion(Guid usuarioId) => $"recuperacion:{usuarioId}";

    /// <summary>La invitación del token si aún sirve (pendiente, enlace y acceso vigentes, organización activa).</summary>
    public async Task<Invitacion?> InvitacionValidaAsync(string? token, CancellationToken ct)
    {
        var hash = TokensEnlace.HashSiValido(token);
        if (hash is null)
        {
            return null;
        }
        var ahora = reloj.GetUtcNow();
        var invitacion = await plataforma.Invitaciones.AsNoTracking().FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (invitacion is null || !TokensEnlace.Coincide(invitacion.TokenHash, hash) || invitacion.Estado != EstadoInvitacion.Pendiente
            || invitacion.TokenVenceEn <= ahora || invitacion.AccesoVenceEn <= ahora
            || !await plataforma.Organizaciones.AnyAsync(o => o.Id == invitacion.OrganizacionId && o.Estado == EstadoOrganizacion.Activa, ct))
        {
            return null;
        }
        return invitacion;
    }

    /// <summary>Registra un intento con un enlace que no sirve (sin el token: solo si era de una invitación conocida).</summary>
    public async Task RechazarEnlaceAsync(string tipo, string? token, CancellationToken ct)
    {
        var hash = TokensEnlace.HashSiValido(token);
        var conocida = hash is null ? null : await plataforma.Invitaciones.AsNoTracking()
            .Where(i => i.TokenHash == hash).Select(i => new { i.Id, i.OrganizacionId, i.Estado }).FirstOrDefaultAsync(ct);
        await registro.RegistrarAsync(TiposEventoSeguridad.EnlaceRechazado, new DatosEventoSeguridad
        {
            OrganizacionId = conocida?.OrganizacionId,
            RecursoTipo = conocida is null ? null : "invitacion",
            RecursoId = conocida?.Id,
            Detalle = new { enlace = tipo, conocido = conocida is not null, estado = conocida?.Estado.ToString().ToLowerInvariant() },
        }, ct);
    }

    public async Task<InvitacionPublica> PublicaAsync(Invitacion invitacion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invitacion);
        var organizacion = await plataforma.Organizaciones.Where(o => o.Id == invitacion.OrganizacionId).Select(o => o.Nombre).SingleAsync(ct);
        var rol = await plataforma.Roles.Where(r => r.Id == invitacion.RolId).Select(r => new { r.Codigo, r.Nombre }).SingleAsync(ct);
        return new InvitacionPublica(invitacion.Correo, invitacion.Nombre, organizacion, rol.Codigo, rol.Nombre,
            invitacion.AccesoVenceEn, invitacion.TokenVenceEn, DocumentosLegales.Vigentes);
    }

    /// <summary>
    /// Acepta la invitación: crea la persona externa y su usuario con el factor elegido, guarda los códigos de recuperación
    /// y la evidencia de los términos, asigna el rol con <see cref="OrigenAsignacion.Invitacion"/> y abre la sesión.
    /// </summary>
    public async Task<ResultadoCuenta<CuentaLista>> AceptarAsync(SolicitudAceptacion solicitud, HttpContext http, SesionExterna sesion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(sesion);
        var invitacion = await InvitacionValidaAsync(solicitud.Token, ct);
        if (invitacion is null)
        {
            await RechazarEnlaceAsync("invitacion", solicitud.Token, ct);
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status410Gone, EnlaceNoValido, MensajeEnlace);
        }
        var nombre = (solicitud.Nombre ?? string.Empty).Trim();
        if (nombre.Length is 0 or > 200)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, "nombre_invalido", "Escribe tu nombre completo (hasta 200 caracteres).");
        }
        if (!DocumentosLegales.AceptaTodo(solicitud.Documentos))
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, "terminos_no_aceptados",
                "Para crear la cuenta debes aceptar la versión vigente de los términos de uso y del aviso de privacidad.");
        }
        if (solicitud.Factor is null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, "metodo_invalido", "Elige passkey o contraseña.");
        }
        var paraValidar = new UsuarioAtlas { PersonaId = Guid.Empty, Email = invitacion.Correo, UserName = invitacion.Correo };
        var (factor, errorFactor) = await factores.PrepararAsync(solicitud.Factor, AmbitoInvitacion(invitacion.Id), paraValidar, http);
        if (errorFactor is not null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, errorFactor.Codigo, errorFactor.Mensaje);
        }

        var rol = await plataforma.Roles.Where(r => r.Id == invitacion.RolId).Select(r => r.Codigo).SingleAsync(ct);
        var alcance = JsonSerializer.Deserialize<List<EndpointsRoles.AlcanceVista>>(invitacion.Alcance) ?? [];
        UsuarioAtlas? usuario = null;
        var resultado = await TransaccionCompartida.EjecutarAsync(plataforma, identidad, async () =>
        {
            // Un solo uso aunque lleguen dos aceptaciones a la vez: la fila queda bloqueada hasta el final.
            var bloqueada = await plataforma.Invitaciones
                .FromSql($"SELECT * FROM atlas.plataforma_invitacion WHERE id = {invitacion.Id} FOR UPDATE")
                .IgnoreQueryFilters().SingleAsync(ct);
            if (bloqueada.Estado != EstadoInvitacion.Pendiente || !TokensEnlace.Coincide(bloqueada.TokenHash, invitacion.TokenHash))
            {
                return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status410Gone, EnlaceNoValido, MensajeEnlace));
            }
            if (await plataforma.Personas.AnyAsync(p => p.Correo == invitacion.Correo, ct))
            {
                return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status409Conflict, "correo_no_disponible",
                    "Ya existe una cuenta de Atlas con este correo. Si es tuya, recupérala desde la pantalla de ingreso."));
            }

            var ahora = reloj.GetUtcNow();
            var persona = new Persona { Tipo = TipoPersona.Externo, OrganizacionId = invitacion.OrganizacionId, Nombre = nombre, Correo = invitacion.Correo };
            plataforma.Personas.Add(persona);
            await plataforma.SaveChangesAsync(ct);

            usuario = new UsuarioAtlas
            {
                PersonaId = persona.Id,
                UserName = persona.Correo,
                Email = persona.Correo,
                // Abrir el enlace del correo prueba que el correo es suyo.
                EmailConfirmed = true,
                CreadoEn = ahora,
            };
            if (factor is PasskeyPreparada passkey)
            {
                usuario.Id = passkey.UsuarioId;
            }
            Exigir(await usuarios.CreateAsync(usuario));
            if (await factores.AplicarAsync(usuario, factor!) is { } error)
            {
                return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, error.Codigo, error.Mensaje));
            }
            var codigos = await factores.NuevosCodigosAsync(usuario);

            await legales.RegistrarAsync(persona.Id, invitacion.Id, http, ct);
            await plataforma.SaveChangesAsync(ct);

            foreach (var item in alcance.Count == 0 ? new List<EndpointsRoles.AlcanceVista> { new(TiposAlcance.Global, null) } : alcance)
            {
                var asignada = await roles.AsignarAsync(persona.Id, new PedidoAsignacion(rol, invitacion.OrganizacionId, item.Tipo, item.Id,
                    ahora, invitacion.AccesoVenceEn), invitacion.InvitadoPor, OrigenAsignacion.Invitacion, ct);
                if (asignada.Error is { } errorRol)
                {
                    return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status409Conflict, errorRol.Codigo, errorRol.Mensaje));
                }
            }

            bloqueada.Estado = EstadoInvitacion.Aceptada;
            bloqueada.PersonaId = persona.Id;
            bloqueada.AceptadaEn = ahora;
            await plataforma.SaveChangesAsync(ct);

            var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = persona.Id, OrganizacionId = persona.OrganizacionId };
            await registro.RegistrarAsync(TiposEventoSeguridad.CuentaCreada, datos with { Detalle = new { invitacion_id = invitacion.Id } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = factor!.Metodo } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = "codigos_recuperacion", cantidad = codigos.Count } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.TerminosAceptados, datos with
            {
                Detalle = new { documentos = DocumentosLegales.Vigentes.Select(d => new { d.Documento, d.Version, d.Hash }) },
            }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.InvitacionAceptada, datos with
            {
                RecursoTipo = "invitacion",
                RecursoId = invitacion.Id,
                Detalle = new { invitacion_id = invitacion.Id, rol, alcances = Math.Max(alcance.Count, 1) },
            }, ct);
            return (true, ResultadosCuenta.Ok<CuentaLista>(new CuentaLista(codigos, false)));
        }, ct);

        if (resultado.Error is not null)
        {
            if (resultado.Error.Error == "codigo_invalido")
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.MfaFallido, new DatosEventoSeguridad
                {
                    OrganizacionId = invitacion.OrganizacionId,
                    RecursoTipo = "invitacion",
                    RecursoId = invitacion.Id,
                    Detalle = new { metodo = "totp", paso = "registro" },
                }, ct);
            }
            return resultado;
        }
        await factores.OlvidarAsync(factor!);
        await sesion.IniciarAsync(http, usuario!, factor!.Metodo, ct);
        return ResultadosCuenta.Ok<CuentaLista>(resultado.Valor! with { SesionIniciada = true });
    }

    // --- Recuperación de la cuenta ---

    /// <summary>
    /// Pide la recuperación: si el correo es de una cuenta externa, envía el enlace (de un solo uso, 30 minutos). La
    /// respuesta es la misma exista o no la cuenta (sin enumeración), también en el tiempo: el correo sale de la cola en
    /// memoria, después de responder (F4-SEC-04, ATLAS-ADR-0049).
    /// </summary>
    public async Task SolicitarRecuperacionAsync(string? correo, HttpContext http, CancellationToken ct)
    {
        var normalizado = (correo ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizado.Length is 0 or > 320)
        {
            return;
        }
        var usuario = await usuarios.FindByEmailAsync(normalizado);
        var persona = usuario is null ? null : await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == usuario.PersonaId, ct);
        if (usuario is null || persona is null || persona.Tipo != TipoPersona.Externo || persona.Estado != EstadoPersona.Activa)
        {
            return;
        }
        var desde = reloj.GetUtcNow() - TimeSpan.FromHours(1);
        var recientes = await identidad.EventosSeguridad.CountAsync(
            e => e.UsuarioId == usuario.Id && e.Tipo == TiposEventoSeguridad.RecuperacionSolicitada && e.OcurridoEn > desde, ct);
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = persona.Id, OrganizacionId = persona.OrganizacionId };
        if (recientes >= opciones.Value.RecuperacionesPorHora)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.RecuperacionSolicitada, datos with { Detalle = new { enviada = false, motivo = "limite" } }, ct);
            return;
        }
        var token = await usuarios.GenerateUserTokenAsync(usuario, TokenOptions.DefaultProvider, PropositoRecuperacion);
        var mensaje = correos.Recuperacion(usuario.Email!, persona.Nombre, Enlace(usuario, token),
            reloj.GetUtcNow() + opciones.Value.VigenciaRecuperacion, forzada: false, http);
        var encolada = colaCorreos.Encolar(new CorreoPendiente(mensaje, datos, "recuperacion"));
        await registro.RegistrarAsync(TiposEventoSeguridad.RecuperacionSolicitada, datos with { Detalle = new { encolada } }, ct);
    }

    /// <summary>El usuario del enlace de recuperación si el enlace aún sirve (token de Identity ligado al sello de seguridad).</summary>
    public async Task<UsuarioAtlas?> UsuarioDeRecuperacionAsync(string? enlace)
    {
        var partes = (enlace ?? string.Empty).Split('.', 2);
        if (partes.Length != 2 || !Guid.TryParseExact(partes[0], "N", out var id) || partes[1].Length > 1024 || !Base64Url.IsValid(partes[1]))
        {
            return null;
        }
        var usuario = await usuarios.FindByIdAsync(id.ToString());
        if (usuario is null)
        {
            return null;
        }
        var token = Convert.ToBase64String(Base64Url.DecodeFromChars(partes[1]));
        return await usuarios.VerifyUserTokenAsync(usuario, TokenOptions.DefaultProvider, PropositoRecuperacion, token) ? usuario : null;
    }

    /// <summary>Quita los factores anteriores, registra el nuevo y cierra todas las sesiones; luego abre una nueva.</summary>
    public async Task<ResultadoCuenta<CuentaLista>> CompletarRecuperacionAsync(
        string? enlace, SolicitudFactor? solicitud, HttpContext http, SesionExterna sesion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sesion);
        var usuario = await UsuarioDeRecuperacionAsync(enlace);
        if (usuario is null)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.EnlaceRechazado, new DatosEventoSeguridad { Detalle = new { enlace = "recuperacion" } }, ct);
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status410Gone, EnlaceNoValido,
                "Este enlace ya se usó o venció. Pide uno nuevo desde la pantalla de ingreso.");
        }
        if (solicitud is null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, "metodo_invalido", "Elige passkey o contraseña.");
        }
        var (factor, errorFactor) = await factores.PrepararAsync(solicitud, AmbitoRecuperacion(usuario.Id), usuario, http);
        if (errorFactor is not null)
        {
            return ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, errorFactor.Codigo, errorFactor.Mensaje);
        }
        var persona = await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Id == usuario.PersonaId, ct);
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = persona.Id, OrganizacionId = persona.OrganizacionId };

        var resultado = await TransaccionCompartida.EjecutarAsync(plataforma, identidad, async () =>
        {
            var retirados = await factores.RetirarTodosAsync(usuario);
            if (await factores.AplicarAsync(usuario, factor!) is { } error)
            {
                return (false, ResultadosCuenta.Falla<CuentaLista>(StatusCodes.Status400BadRequest, error.Codigo, error.Mensaje));
            }
            var codigos = await factores.NuevosCodigosAsync(usuario);
            Exigir(await usuarios.SetLockoutEndDateAsync(usuario, null));
            // El sello nuevo invalida este mismo enlace (un solo uso) y las sesiones. Los contadores de fallos ya volvieron a
            // cero al retirar los factores.
            Exigir(await usuarios.UpdateSecurityStampAsync(usuario));
            await registro.RegistrarAsync(TiposEventoSeguridad.FactoresRetirados, datos with { Detalle = new { metodos = retirados, motivo = "recuperacion" } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = factor!.Metodo } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactorRegistrado, datos with { Detalle = new { metodo = "codigos_recuperacion", cantidad = codigos.Count } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.RecuperacionCompletada, datos, ct);
            return (true, ResultadosCuenta.Ok<CuentaLista>(new CuentaLista(codigos, false)));
        }, ct);
        if (resultado.Error is not null)
        {
            return resultado;
        }
        await factores.OlvidarAsync(factor!);
        // Todas las sesiones del BFF y los tokens de OpenIddict (Bearer y refresh) de la persona dejan de servir.
        await invalidador.InvalidarSesionesAsync(persona.Id, MotivosInvalidacion.CambioDeCredenciales, null, ct);
        // Aviso al titular, sin enlaces (F4-SEC-06, ATLAS-ADR-0049): si no fue él, sabe que alguien tiene su buzón.
        if (!colaCorreos.Encolar(new CorreoPendiente(
            CorreosExternos.AvisoRecuperacion(usuario.Email!, persona.Nombre, factor!.Metodo, reloj.GetUtcNow()), datos, "aviso_recuperacion")))
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.CorreoNoEnviado, datos with { Detalle = new { motivo = "aviso_recuperacion" } }, ct);
        }

        // Si puede entrar y no tiene documentos pendientes, la sesión queda abierta; si no, entra por el ingreso normal.
        var actualizado = await usuarios.FindByIdAsync(usuario.Id.ToString());
        var abrir = await sesion.MotivoNoPermitidoAsync(persona.Id, ct) is null && (await legales.PendientesAsync(persona.Id, ct)).Count == 0;
        if (abrir)
        {
            await sesion.IniciarAsync(http, actualizado!, factor!.Metodo, ct);
        }
        return ResultadosCuenta.Ok<CuentaLista>(resultado.Valor! with { SesionIniciada = abrir });
    }

    /// <summary>
    /// Restablecimiento forzado por un administrador (§6.2): quita todos los factores de la cuenta externa, cierra sus
    /// sesiones y le envía un enlace de recuperación para registrar uno nuevo.
    /// </summary>
    public async Task<ResultadoCuenta<bool>> RestablecerAsync(Guid personaId, Guid? actor, HttpContext http, CancellationToken ct)
    {
        var persona = await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personaId, ct);
        if (persona is null)
        {
            return ResultadosCuenta.Falla<bool>(StatusCodes.Status404NotFound, "persona_no_encontrada", "La persona no existe.");
        }
        if (persona.Tipo != TipoPersona.Externo)
        {
            return ResultadosCuenta.Falla<bool>(StatusCodes.Status409Conflict, "persona_no_externa",
                "Los empleados entran con Microsoft: sus factores se administran en Entra ID.");
        }
        var usuario = await usuarios.Users.FirstOrDefaultAsync(u => u.PersonaId == personaId, ct);
        if (usuario is null)
        {
            return ResultadosCuenta.Falla<bool>(StatusCodes.Status409Conflict, "sin_cuenta", "La persona aún no ha creado su cuenta.");
        }
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = personaId, OrganizacionId = persona.OrganizacionId, ActorPersonaId = actor };
        await TransaccionCompartida.EjecutarAsync(plataforma, identidad, async () =>
        {
            var retirados = await factores.RetirarTodosAsync(usuario);
            await registro.RegistrarAsync(TiposEventoSeguridad.FactoresRetirados, datos with { Detalle = new { metodos = retirados, motivo = "restablecimiento" } }, ct);
            await registro.RegistrarAsync(TiposEventoSeguridad.RestablecimientoForzado, datos, ct);
            return (true, true);
        }, ct);
        await invalidador.InvalidarSesionesAsync(personaId, MotivosInvalidacion.CambioDeCredenciales, actor, ct);
        var actualizado = await usuarios.FindByIdAsync(usuario.Id.ToString());
        var enviado = await EnviarRecuperacionAsync(actualizado!, persona.Nombre, forzada: true, http, ct);
        if (!enviado)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.CorreoNoEnviado, datos with { Detalle = new { motivo = "restablecimiento" } }, ct);
        }
        return ResultadosCuenta.Ok<bool>(enviado);
    }

    private async Task<bool> EnviarRecuperacionAsync(UsuarioAtlas usuario, string nombre, bool forzada, HttpContext http, CancellationToken ct)
    {
        var token = await usuarios.GenerateUserTokenAsync(usuario, TokenOptions.DefaultProvider, PropositoRecuperacion);
        var vence = reloj.GetUtcNow() + opciones.Value.VigenciaRecuperacion;
        return await correos.EnviarRecuperacionAsync(usuario.Email!, nombre, Enlace(usuario, token), vence, forzada, http, ct);
    }

    /// <summary>Fragmento del enlace: id del usuario y token de Identity (ligado al sello de seguridad).</summary>
    private static string Enlace(UsuarioAtlas usuario, string token) => $"{usuario.Id:N}.{Base64Url.EncodeToString(Convert.FromBase64String(token))}";

    private static void Exigir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException("Identity rechazó el cambio: " + string.Join("; ", resultado.Errors.Select(e => e.Code)));
        }
    }
}
