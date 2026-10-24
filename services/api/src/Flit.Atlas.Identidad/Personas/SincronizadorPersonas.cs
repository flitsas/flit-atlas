using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Personas;

/// <summary>Quién pidió la corrida.</summary>
public enum DisparoSincronizacion
{
    Programada,
    Manual,
}

/// <summary>Conteos de una corrida de la sincronización de personas. Sin datos personales: van a la auditoría y al log.</summary>
[Description("Resultado de la última corrida de la sincronización de personas con Microsoft Graph. Solo conteos, sin datos personales.")]
public sealed record EstadoSincronizacion(
    [property: Description("Id de la corrida (recurso_id del evento de auditoría).")] Guid Id,
    [property: Description("programada o manual.")] string Disparo,
    [property: Description("enlazadas, grupo o tenant.")] string Origen,
    [property: Description("exitosa, fallida o en_curso.")] string Resultado,
    DateTimeOffset Inicio,
    DateTimeOffset? Fin,
    [property: Description("Personas nuevas en Atlas.")] int Creadas,
    [property: Description("Personas con algún dato cambiado (nombre, cargo, área, teléfono o foto).")] int Actualizadas,
    [property: Description("Personas que quedaron inactivas porque su cuenta está deshabilitada o ya no existe en Entra; sus sesiones se cerraron.")] int Deshabilitadas,
    [property: Description("Personas que volvieron a activa porque su cuenta se rehabilitó en Entra.")] int Reactivadas,
    [property: Description("Personas leídas sin cambios.")] int SinCambios,
    [property: Description("Usuarios de Entra que no se trajeron: invitados, sin correo, deshabilitados que Atlas no conocía o con un correo que ya es de otra persona.")] int Omitidas,
    [property: Description("Deshabilitaciones que el freno de seguridad retuvo (más de la fracción configurada en una sola corrida); no se aplicó ninguna.")] int Retenidas,
    [property: Description("Tipo de la excepción si la corrida falló (sin el mensaje, que podría traer datos).")] string? Error);

/// <summary>Otra réplica (u otra petición) tiene el candado de la sincronización.</summary>
public sealed class SincronizacionEnCursoException() : InvalidOperationException("La sincronización de personas ya está en curso.");

/// <summary>
/// Una corrida de la sincronización diaria de personas con Microsoft Graph (HU 2.2, ATLAS-ADR-0030): nombre, cargo, área,
/// teléfono, foto y estado de los empleados. Una cuenta deshabilitada (o borrada) en Entra deja la persona
/// <see cref="EstadoPersona.Inactiva"/> y le cierra las sesiones con <see cref="IInvalidadorSesiones"/>; al rehabilitarse,
/// vuelve. Nunca toca a los externos, a la cuenta de emergencia (ATLAS-ADR-0027) ni a una persona suspendida desde la
/// administración. Un candado consultivo de PostgreSQL deja correr una sola a la vez en todas las réplicas.
/// </summary>
public sealed partial class SincronizadorPersonas(
    PlataformaDbContext plataforma,
    IDirectorioEntra directorio,
    IInvalidadorSesiones invalidador,
    RegistroSeguridad registro,
    IOptions<OpcionesSincronizacionPersonas> opciones,
    TimeProvider reloj,
    ILogger<SincronizadorPersonas> log)
{
    /// <summary>Llave del candado consultivo de sesión (<c>pg_try_advisory_lock</c>) de esta sincronización.</summary>
    internal const long Candado = 0x41544C4153_0030;

    /// <summary>Clave de <c>plataforma_configuracion</c> con el estado de la última corrida.</summary>
    public const string ClaveEstado = "identidad.sincronizacion_personas";

    public const string AccionAuditoria = "identidad.personas_sincronizadas";

    public const string MotivoInvalidacion = MotivosInvalidacion.CuentaDeshabilitadaEnEntra;

    internal static readonly Meter Medidor = new("Flit.Atlas.Identidad");

    private static readonly Counter<int> Cambios = Medidor.CreateCounter<int>(
        "atlas.personas.sincronizacion.cambios", description: "Personas creadas, actualizadas, deshabilitadas o reactivadas por la sincronización con Graph.");

    private static readonly Counter<int> Corridas = Medidor.CreateCounter<int>(
        "atlas.personas.sincronizacion.corridas", description: "Corridas de la sincronización de personas por resultado.");

    private static readonly Histogram<double> Duracion = Medidor.CreateHistogram<double>(
        "atlas.personas.sincronizacion.duracion", unit: "s", description: "Duración de las corridas de la sincronización de personas.");

    private const int LargoNombre = 200;
    private const int LargoTelefono = 50;

    /// <summary>
    /// Corre una sincronización completa. Lanza <see cref="SincronizacionEnCursoException"/> si otra tiene el candado, y
    /// las fallas de Graph o de la base (el trabajo programado las reintenta).
    /// </summary>
    /// <param name="noAntesDe">
    /// Solo el trabajo programado: si una corrida exitosa ya empezó en o después de este momento (otra réplica, en la
    /// misma franja), no corre otra y devuelve <c>null</c>.
    /// </param>
    /// <param name="actor">Persona que la pidió con «Sincronizar ahora»; <c>null</c> si fue el trabajo programado.</param>
    public async Task<EstadoSincronizacion?> SincronizarAsync(
        DisparoSincronizacion disparo, Guid? actor = null, DateTimeOffset? noAntesDe = null, CancellationToken ct = default)
    {
        if (!directorio.Disponible)
        {
            throw new InvalidOperationException("Sin Entra ID configurado no hay directorio que sincronizar.");
        }
        var config = opciones.Value;
        await plataforma.Database.OpenConnectionAsync(ct);
        try
        {
            var tomado = await plataforma.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({Candado}) AS \"Value\"").SingleAsync(ct);
            if (!tomado)
            {
                throw new SincronizacionEnCursoException();
            }
            try
            {
                if (noAntesDe is { } franja && await LeerEstadoAsync(plataforma, ct) is { Resultado: Resultados.Exitosa } ultima
                    && ultima.Inicio >= franja)
                {
                    return null;
                }
                return await CorrerAsync(disparo, actor, config, ct);
            }
            finally
            {
                await plataforma.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Candado})", CancellationToken.None);
            }
        }
        finally
        {
            await plataforma.Database.CloseConnectionAsync();
        }
    }

    private async Task<EstadoSincronizacion> CorrerAsync(
        DisparoSincronizacion disparo, Guid? actor, OpcionesSincronizacionPersonas config, CancellationToken ct)
    {
        var cronometro = Stopwatch.StartNew();
        var inicio = reloj.GetUtcNow();
        var conteo = new Conteo();
        var origen = Texto(config.Origen);
        var id = Guid.CreateVersion7();
        try
        {
            var (deshabilitar, conRolesCambiados, sinVincular) = await AplicarAsync(config, inicio, conteo, ct);

            // Después de guardar el estado (ATLAS-ADR-0022): un ingreso simultáneo o ve la persona inactiva o queda con el
            // sello viejo.
            foreach (var personaId in deshabilitar)
            {
                await invalidador.InvalidarSesionesAsync(personaId, MotivoInvalidacion, null, ct);
            }
            // Roles de grupos de Entra recalculados (F4-SEC-08, ATLAS-ADR-0049): quien salió de un grupo pierde el rol ya, con
            // sus sesiones y sus refresh, sin esperar a que vuelva a entrar.
            foreach (var personaId in conRolesCambiados)
            {
                await invalidador.InvalidarSesionesAsync(personaId, MotivosInvalidacion.CambioDeRoles, null, ct);
            }
            foreach (var personaId in sinVincular)
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.VinculoPorCorreoRechazado, new DatosEventoSeguridad
                {
                    PersonaId = personaId,
                    Detalle = new { origen = "sincronizacion", motivo = "persona_con_roles" },
                }, ct);
            }

            var estado = conteo.Estado(id, Texto(disparo), origen, Resultados.Exitosa, inicio, reloj.GetUtcNow(), null);
            plataforma.ChangeTracker.Clear();
            plataforma.Auditoria.Add(AdministracionRoles.CrearEvento(estado.Fin!.Value, AccionAuditoria, "sincronizacion_personas", id,
                null, actor, new
                {
                    disparo = estado.Disparo,
                    origen,
                    creadas = estado.Creadas,
                    actualizadas = estado.Actualizadas,
                    deshabilitadas = estado.Deshabilitadas,
                    reactivadas = estado.Reactivadas,
                    sin_cambios = estado.SinCambios,
                    omitidas = estado.Omitidas,
                    retenidas = estado.Retenidas,
                    roles_recalculados = conRolesCambiados.Count,
                    sin_vincular = sinVincular.Count,
                    duracion_ms = (long)cronometro.Elapsed.TotalMilliseconds,
                }));
            await GuardarEstadoAsync(plataforma, estado, ct);

            Corridas.Add(1, new KeyValuePair<string, object?>("resultado", Resultados.Exitosa));
            Medir("creada", estado.Creadas);
            Medir("actualizada", estado.Actualizadas);
            Medir("deshabilitada", estado.Deshabilitadas);
            Medir("reactivada", estado.Reactivadas);
            Duracion.Record(cronometro.Elapsed.TotalSeconds);
            Terminada(log, estado.Disparo, origen, estado.Creadas, estado.Actualizadas, estado.Deshabilitadas, estado.Reactivadas,
                estado.SinCambios, estado.Omitidas);
            if (estado.Retenidas > 0)
            {
                FrenoActivado(log, estado.Retenidas);
            }
            return estado;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Corridas.Add(1, new KeyValuePair<string, object?>("resultado", Resultados.Fallida));
            Duracion.Record(cronometro.Elapsed.TotalSeconds);
            // Si la base responde, queda el último intento fallido para la pantalla de administración. Si no, igual se
            // reintenta: el error original es el que importa.
            try
            {
                plataforma.ChangeTracker.Clear();
                await GuardarEstadoAsync(plataforma,
                    conteo.Estado(id, Texto(disparo), origen, Resultados.Fallida, inicio, reloj.GetUtcNow(), ex.GetType().Name), ct);
            }
#pragma warning disable CA1031 // Registrar el fallo no debe tapar el error de la corrida.
            catch (Exception)
#pragma warning restore CA1031
            {
                plataforma.ChangeTracker.Clear();
            }
            throw;
        }
    }

    /// <summary>
    /// Lee Graph y aplica los cambios en una sola transacción. Devuelve las personas deshabilitadas y las que cambiaron de
    /// roles por grupos (a las dos hay que cerrarles la sesión) y las que no se enlazaron por correo porque tienen roles.
    /// </summary>
    private async Task<(List<Guid> Deshabilitar, List<Guid> ConRolesCambiados, List<Guid> SinVincular)> AplicarAsync(
        OpcionesSincronizacionPersonas config, DateTimeOffset ahora, Conteo conteo, CancellationToken ct)
    {
        // 1. Lo que dice Entra (antes de tocar la base: si Graph falla a mitad, no queda nada a medias).
        var listados = new Dictionary<Guid, UsuarioEntra>();
        await foreach (var usuario in directorio.ListarAsync(config.Origen, config.GrupoEmpleados, ct))
        {
            if (!usuario.EsMiembro)
            {
                conteo.Omitidas++;
                continue;
            }
            listados.TryAdd(usuario.Id, usuario);
        }

        var emergencia = await plataforma.AsignacionesRol
            .Where(a => a.Origen == OrigenAsignacion.Emergencia).Select(a => a.PersonaId).Distinct().ToListAsync(ct);
        var empleados = await plataforma.Personas
            .Where(p => p.Tipo == TipoPersona.Empleado && !emergencia.Contains(p.Id))
            .ToListAsync(ct);
        var porOid = empleados.Where(p => p.EntraOid is not null).ToDictionary(p => p.EntraOid!.Value);
        var porCorreo = empleados.Where(p => p.EntraOid is null).ToDictionary(p => p.Correo, StringComparer.Ordinal);
        // Una persona sin oid con roles asignados no se enlaza por correo (F4-SEC-07, ATLAS-ADR-0049): el correo de Entra
        // se cambia y se recicla, y quien lo tuviera heredaría esos roles. Queda un evento para que un administrador decida.
        var conRoles = (await plataforma.AsignacionesRol.Select(a => a.PersonaId).Distinct().ToListAsync(ct)).ToHashSet();
        var sinVincular = new List<Guid>();
        var correosOcupados = (await plataforma.Personas.Select(p => p.Correo).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        // Las enlazadas que el listado no trajo (o todas, con el origen «enlazadas») se consultan una por una: un 404 es una
        // cuenta borrada en Entra, que cuenta como deshabilitada.
        var estados = new Dictionary<Guid, UsuarioEntra?>();
        foreach (var (oid, _) in porOid)
        {
            estados[oid] = listados.TryGetValue(oid, out var listado) ? listado : await directorio.ObtenerAsync(oid, ct);
        }

        // 2. Personas nuevas o enlazadas por correo: solo cuentas habilitadas con buzón (una deshabilitada que Atlas no
        //    conocía no le sirve a nadie, y una sin buzón suele ser de servicio). El correo de Entra solo enlaza un empleado sin oid, como en el primer ingreso.
        Guid? organizacionInterna = null;
        var nuevas = new List<(Persona Persona, UsuarioEntra Usuario)>();
        foreach (var usuario in listados.Values.Where(u => !porOid.ContainsKey(u.Id)))
        {
            if (usuario.Correo is { } correo && porCorreo.Remove(correo, out var existente))
            {
                if (conRoles.Contains(existente.Id))
                {
                    sinVincular.Add(existente.Id);
                    conteo.Omitidas++;
                    continue;
                }
                existente.EntraOid = usuario.Id;
                porOid[usuario.Id] = existente;
                estados[usuario.Id] = usuario;
                continue;
            }
            if (!usuario.Habilitada || !usuario.ConBuzon || usuario.Correo is null || correosOcupados.Contains(usuario.Correo))
            {
                conteo.Omitidas++;
                continue;
            }
            organizacionInterna ??= await OrganizacionInternaAsync(ct);
            var persona = new Persona
            {
                Tipo = TipoPersona.Empleado,
                OrganizacionId = organizacionInterna.Value,
                Nombre = Recortar(usuario.Nombre, LargoNombre)!,
                Correo = usuario.Correo,
                EntraOid = usuario.Id,
            };
            plataforma.Personas.Add(persona);
            correosOcupados.Add(persona.Correo);
            nuevas.Add((persona, usuario));
        }

        // 3. Datos, foto, estado y roles de grupos de cada persona enlazada. Los grupos solo se leen si algún grupo da roles
        //    o alguien tiene roles de un grupo (una consulta a Graph por persona).
        var leerGrupos = await plataforma.MapeosGrupoEntra.AnyAsync(ct)
            || await plataforma.AsignacionesRol.AnyAsync(a => a.Origen == OrigenAsignacion.GrupoEntra, ct);
        var conRolesCambiados = new List<Guid>();
        var activasEnlazadas = porOid.Values.Count(p => p.Estado == EstadoPersona.Activa);
        var porDeshabilitar = new List<Persona>();
        foreach (var (oid, persona) in porOid)
        {
            var usuario = estados[oid];
            var cambio = false;
            if (usuario is not null)
            {
                cambio |= Actualizar(persona, usuario);
                if (usuario.Habilitada)
                {
                    cambio |= await ActualizarFotoAsync(persona, config.TamanoMaximoFoto, ahora, ct);
                    if (leerGrupos && persona.Estado != EstadoPersona.Suspendida && await RolesDeGruposEntra.SincronizarAsync(plataforma,
                        persona.Id, persona.OrganizacionId, await directorio.ObtenerGruposAsync(oid, ct), ahora, ct))
                    {
                        conRolesCambiados.Add(persona.Id);
                    }
                }
            }
            persona.SincronizadaEn = ahora;

            var habilitada = usuario?.Habilitada == true;
            if (!habilitada && persona.Estado == EstadoPersona.Activa)
            {
                porDeshabilitar.Add(persona);
            }
            else if (habilitada && persona.Estado == EstadoPersona.Inactiva && persona.InactivaPorEntra)
            {
                persona.Estado = EstadoPersona.Activa;
                persona.InactivaPorEntra = false;
                conteo.Reactivadas++;
            }
            else if (cambio)
            {
                conteo.Actualizadas++;
            }
            else
            {
                conteo.SinCambios++;
            }
        }
        foreach (var (persona, usuario) in nuevas)
        {
            Actualizar(persona, usuario);
            await ActualizarFotoAsync(persona, config.TamanoMaximoFoto, ahora, ct);
            persona.SincronizadaEn = ahora;
            conteo.Creadas++;
        }

        // Freno: demasiadas cuentas «deshabilitadas» de una vez es más probable un error de configuración que una
        // desbandada. No se deshabilita a nadie y queda en la auditoría.
        var tope = Math.Max(config.MinimoFreno, (int)Math.Floor(activasEnlazadas * config.FraccionMaximaDeshabilitadas));
        var deshabilitar = new List<Guid>();
        if (porDeshabilitar.Count > tope)
        {
            conteo.Retenidas = porDeshabilitar.Count;
        }
        else
        {
            foreach (var persona in porDeshabilitar)
            {
                persona.Estado = EstadoPersona.Inactiva;
                persona.InactivaPorEntra = true;
                deshabilitar.Add(persona.Id);
                conteo.Deshabilitadas++;
            }
        }

        await using var transaccion = await plataforma.Database.BeginTransactionAsync(ct);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return (deshabilitar, [.. conRolesCambiados.Except(deshabilitar)], sinVincular);
    }

    /// <summary>Nombre, cargo, área y teléfono como los tiene Entra; <c>true</c> si alguno cambió.</summary>
    private static bool Actualizar(Persona persona, UsuarioEntra usuario)
    {
        var nombre = Recortar(usuario.Nombre, LargoNombre)!;
        var cargo = Recortar(usuario.Cargo, LargoNombre);
        var area = Recortar(usuario.Area, LargoNombre);
        var telefono = Recortar(usuario.Telefono, LargoTelefono);
        var cambio = persona.Nombre != nombre || persona.Cargo != cargo || persona.Area != area || persona.Telefono != telefono;
        persona.Nombre = nombre;
        persona.Cargo = cargo;
        persona.Area = area;
        persona.Telefono = telefono;
        return cambio;
    }

    /// <summary>Guarda la foto si cambió su hash (o la borra si ya no tiene); <c>true</c> si cambió.</summary>
    private async Task<bool> ActualizarFotoAsync(Persona persona, int tamanoMaximo, DateTimeOffset ahora, CancellationToken ct)
    {
        var foto = await directorio.ObtenerFotoAsync(persona.EntraOid!.Value, tamanoMaximo, ct);
        var hash = foto is null ? null : Convert.ToHexStringLower(SHA256.HashData(foto.Contenido));
        if (hash == persona.FotoHash)
        {
            return false;
        }

        var actual = await plataforma.FotosPersonas.FirstOrDefaultAsync(f => f.PersonaId == persona.Id, ct);
        if (foto is null)
        {
            if (actual is not null)
            {
                plataforma.FotosPersonas.Remove(actual);
            }
        }
        else if (actual is null)
        {
            plataforma.FotosPersonas.Add(new PersonaFoto
            {
                PersonaId = persona.Id,
                Contenido = foto.Contenido,
                TipoContenido = foto.TipoContenido,
                Hash = hash!,
                ActualizadaEn = ahora,
            });
        }
        else
        {
            actual.Contenido = foto.Contenido;
            actual.TipoContenido = foto.TipoContenido;
            actual.Hash = hash!;
            actual.ActualizadaEn = ahora;
        }
        persona.FotoHash = hash;
        return true;
    }

    private async Task<Guid> OrganizacionInternaAsync(CancellationToken ct)
    {
        var interna = await plataforma.Organizaciones.Where(o => o.Tipo == TipoOrganizacion.Interna).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        if (interna is { } id)
        {
            return id;
        }
        var nueva = new Organizacion { Nombre = AprovisionadorEmpleados.NombreOrganizacionInterna, Tipo = TipoOrganizacion.Interna };
        plataforma.Organizaciones.Add(nueva);
        return nueva.Id;
    }

    /// <summary>Estado de la última corrida (o de la que está en curso en otra réplica, si es la que escribió último).</summary>
    public static async Task<EstadoSincronizacion?> LeerEstadoAsync(PlataformaDbContext plataforma, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plataforma);
        var valor = await plataforma.Configuraciones.AsNoTracking().Where(c => c.Clave == ClaveEstado).Select(c => c.Valor).FirstOrDefaultAsync(ct);
        return valor is null ? null : JsonSerializer.Deserialize<EstadoSincronizacion>(valor, Json);
    }

    private async Task GuardarEstadoAsync(PlataformaDbContext db, EstadoSincronizacion estado, CancellationToken ct)
    {
        var fila = await db.Configuraciones.FirstOrDefaultAsync(c => c.Clave == ClaveEstado, ct);
        var valor = JsonSerializer.Serialize(estado, Json);
        if (fila is null)
        {
            db.Configuraciones.Add(new Configuracion { Clave = ClaveEstado, Valor = valor, ActualizadoEn = reloj.GetUtcNow() });
        }
        else
        {
            fila.Valor = valor;
            fila.ActualizadoEn = reloj.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string? Recortar(string? valor, int largo) => valor is null || valor.Length <= largo ? valor : valor[..largo];

    internal static string Texto(OrigenPersonas origen) => origen switch
    {
        OrigenPersonas.Grupo => "grupo",
        OrigenPersonas.Tenant => "tenant",
        _ => "enlazadas",
    };

    private static string Texto(DisparoSincronizacion disparo) => disparo == DisparoSincronizacion.Manual ? "manual" : "programada";

    private static void Medir(string cambio, int cantidad)
    {
        if (cantidad > 0)
        {
            Cambios.Add(cantidad, new KeyValuePair<string, object?>("cambio", cambio));
        }
    }

    internal static class Resultados
    {
        public const string Exitosa = "exitosa";
        public const string Fallida = "fallida";
    }

    private sealed class Conteo
    {
        public int Creadas;
        public int Actualizadas;
        public int Deshabilitadas;
        public int Reactivadas;
        public int SinCambios;
        public int Omitidas;
        public int Retenidas;

        public EstadoSincronizacion Estado(
            Guid id, string disparo, string origen, string resultado, DateTimeOffset inicio, DateTimeOffset? fin, string? error) =>
            new(id, disparo, origen, resultado, inicio, fin, Creadas, Actualizadas, Deshabilitadas, Reactivadas, SinCambios, Omitidas,
                Retenidas, error);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sincronización de personas ({Disparo}, {Origen}): {Creadas} creadas, {Actualizadas} actualizadas, {Deshabilitadas} deshabilitadas, {Reactivadas} reactivadas, {SinCambios} sin cambios, {Omitidas} omitidas")]
    private static partial void Terminada(ILogger log, string disparo, string origen, int creadas, int actualizadas, int deshabilitadas,
        int reactivadas, int sinCambios, int omitidas);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sincronización de personas: el freno de seguridad retuvo {Retenidas} deshabilitaciones; revisa la configuración de Graph y el origen")]
    private static partial void FrenoActivado(ILogger log, int retenidas);
}
