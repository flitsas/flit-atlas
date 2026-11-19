using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Autorizacion;

/// <summary>Una asignación pedida: rol, organización, alcance y vigencia (prompt maestro §7).</summary>
/// <param name="Rol">Código del rol (<c>atlas.*</c> o <c>ext.*</c>); los implícitos no se asignan.</param>
/// <param name="OrganizacionId">Organización de la asignación; por defecto la de la persona.</param>
/// <param name="AlcanceTipo">Tipo de alcance (<c>global</c> por defecto); si no es global, <paramref name="AlcanceId"/> es obligatorio.</param>
/// <param name="VigenteDesde">Desde cuándo vale; por defecto, ahora.</param>
/// <param name="VigenteHasta">Hasta cuándo vale; obligatoria para los roles externos.</param>
public sealed record PedidoAsignacion(
    string Rol, Guid? OrganizacionId = null, string? AlcanceTipo = null, string? AlcanceId = null,
    DateTimeOffset? VigenteDesde = null, DateTimeOffset? VigenteHasta = null);

public enum TipoError
{
    NoEncontrado,
    Invalido,
    Conflicto,
}

/// <param name="Campo">Campo del pedido que falla (solo en <see cref="TipoError.Invalido"/>).</param>
public sealed record ErrorAdministracion(TipoError Tipo, string Codigo, string Mensaje, string? Campo = null);

/// <summary>Resultado de una operación de administración: el valor o el error.</summary>
public sealed record Resultado<T>(T? Valor, ErrorAdministracion? Error)
{
    public static implicit operator Resultado<T>(ErrorAdministracion error) => new(default, error);
}

/// <summary>Acciones de la auditoría de roles (<c>plataforma_auditoria</c>).</summary>
public static class AccionesAuditoriaRoles
{
    public const string AsignacionCreada = "plataforma.asignacion_creada";
    public const string AsignacionRetirada = "plataforma.asignacion_retirada";
    public const string AsignacionVencida = "plataforma.asignacion_vencida";
    public const string MapeoCreado = "plataforma.mapeo_grupo_entra_creado";
    public const string MapeoEliminado = "plataforma.mapeo_grupo_entra_eliminado";
}

/// <summary>
/// Administración de asignaciones de roles y de mapeos de grupos de Entra (HU 1.4, ATLAS-ADR-0023). Valida contra el
/// catálogo del código, guarda el cambio y su auditoría en la misma transacción y, si el cambio quita acceso, invalida
/// las sesiones de las personas afectadas <b>después</b> de guardar (ATLAS-ADR-0022). Es también el punto de entrada de
/// la HU 1.5: aceptar una invitación llama a <see cref="AsignarAsync"/> con <see cref="OrigenAsignacion.Invitacion"/>.
/// </summary>
public sealed partial class AdministracionRoles(
    PlataformaDbContext plataforma,
    CatalogoPermisos catalogo,
    IInvalidadorSesiones invalidador,
    IHttpContextAccessor contexto,
    TimeProvider reloj)
{
    /// <summary>Vigencia máxima de un rol externo (§6.2: «máximo configurable, por defecto 90 días»).</summary>
    public static readonly TimeSpan VigenciaMaximaExterna = TimeSpan.FromDays(90);

    public async Task<Resultado<AsignacionRol>> AsignarAsync(
        Guid personaId, PedidoAsignacion pedido, Guid? actor, OrigenAsignacion origen = OrigenAsignacion.Manual, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var persona = await plataforma.Personas.FirstOrDefaultAsync(p => p.Id == personaId, ct);
        if (persona is null)
        {
            return new ErrorAdministracion(TipoError.NoEncontrado, "persona_no_encontrada", "La persona no existe.");
        }
        var definicion = catalogo.Rol(pedido.Rol ?? string.Empty);
        if (definicion is null || definicion.Tipo == TipoRol.Implicito || definicion.Codigo == RolesAtlas.Lector)
        {
            return Invalido("rol", "rol_no_asignable",
                "El rol no existe o no se asigna a mano (atlas.lector lo tiene todo empleado; los implícitos los da la propiedad).");
        }
        var externo = definicion.Tipo == TipoRol.Externo;
        if (externo != (persona.Tipo == TipoPersona.Externo))
        {
            return Invalido("rol", "rol_no_corresponde_a_la_persona",
                externo ? "Un rol externo solo se asigna a una persona externa." : "Un rol interno solo se asigna a un empleado.");
        }

        var organizacion = pedido.OrganizacionId ?? persona.OrganizacionId;
        if (externo && organizacion != persona.OrganizacionId)
        {
            return Invalido("organizacionId", "organizacion_distinta", "Un rol externo queda ligado a la organización de la persona.");
        }
        if (!await plataforma.Organizaciones.AnyAsync(o => o.Id == organizacion, ct))
        {
            return Invalido("organizacionId", "organizacion_no_encontrada", "La organización no existe.");
        }

        var tipoAlcance = string.IsNullOrWhiteSpace(pedido.AlcanceTipo) ? TiposAlcance.Global : pedido.AlcanceTipo.Trim();
        if (!catalogo.TiposAlcance.Contains(tipoAlcance))
        {
            return Invalido("alcanceTipo", "alcance_desconocido", $"Tipos de alcance válidos: {string.Join(", ", catalogo.TiposAlcance)}.");
        }
        var idAlcance = string.IsNullOrWhiteSpace(pedido.AlcanceId) ? null : pedido.AlcanceId.Trim();
        if ((tipoAlcance == TiposAlcance.Global) != (idAlcance is null) || (idAlcance is not null && !IdAlcance().IsMatch(idAlcance)))
        {
            return Invalido("alcanceId", "alcance_invalido",
                "Un alcance global no lleva id; cualquier otro exige el id del recurso (hasta 200 caracteres, sin espacios).");
        }

        var ahora = reloj.GetUtcNow();
        var desde = pedido.VigenteDesde ?? ahora;
        var hasta = pedido.VigenteHasta;
        if (hasta is not null && (hasta <= desde || hasta <= ahora))
        {
            return Invalido("vigenteHasta", "vigencia_invalida", "El vencimiento debe ser posterior al inicio y a la hora actual.");
        }
        if (externo && (hasta is null || hasta - desde > VigenciaMaximaExterna))
        {
            return Invalido("vigenteHasta", "vigencia_externa_obligatoria",
                $"Un rol externo exige vencimiento, a no más de {VigenciaMaximaExterna.TotalDays:0} días de su inicio.");
        }

        var rolId = await plataforma.Roles.Where(r => r.Codigo == definicion.Codigo).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (rolId is null)
        {
            // El migrador no ha aplicado el catálogo: no se inventa el rol aquí.
            return new ErrorAdministracion(TipoError.Conflicto, "rol_no_sembrado", "El rol aún no está en la base; corre el migrador.");
        }
        var duplicada = await plataforma.AsignacionesRol.AnyAsync(a =>
            a.PersonaId == personaId && a.RolId == rolId && a.OrganizacionId == organizacion
            && a.AlcanceTipo == tipoAlcance && a.AlcanceId == idAlcance
            && (a.VigenteHasta == null || a.VigenteHasta > ahora), ct);
        if (duplicada)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "asignacion_duplicada",
                "La persona ya tiene ese rol con el mismo alcance y vigente; retírala antes de cambiar la vigencia.");
        }

        var asignacion = new AsignacionRol
        {
            PersonaId = personaId,
            RolId = rolId.Value,
            OrganizacionId = organizacion,
            AlcanceTipo = tipoAlcance,
            AlcanceId = idAlcance,
            VigenteDesde = desde,
            VigenteHasta = hasta,
            Origen = origen,
            AsignadoPor = actor,
        };
        plataforma.AsignacionesRol.Add(asignacion);
        plataforma.Auditoria.Add(EventoAsignacion(AccionesAuditoriaRoles.AsignacionCreada, asignacion, definicion.Codigo, actor));
        await plataforma.SaveChangesAsync(ct);
        // Sumar un rol no invalida sesiones: los permisos se recalculan en cada petición (ATLAS-ADR-0023).
        return new Resultado<AsignacionRol>(asignacion, null);
    }

    /// <summary>
    /// Retira una asignación manual o de invitación. Las de grupos de Entra no se retiran aquí: se quitan en Entra (o
    /// eliminando el mapeo) y se sincronizan en el siguiente ingreso.
    /// </summary>
    public async Task<Resultado<AsignacionRol>> RetirarAsync(Guid asignacionId, Guid? actor, CancellationToken ct = default)
    {
        var asignacion = await plataforma.AsignacionesRol.FirstOrDefaultAsync(a => a.Id == asignacionId, ct);
        if (asignacion is null)
        {
            return new ErrorAdministracion(TipoError.NoEncontrado, "asignacion_no_encontrada", "La asignación no existe.");
        }
        if (asignacion.Origen == OrigenAsignacion.GrupoEntra)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "asignacion_de_grupo_entra",
                "La asignación viene de un grupo de Entra: quita a la persona del grupo o elimina el mapeo.");
        }
        var rol = await plataforma.Roles.Where(r => r.Id == asignacion.RolId).Select(r => r.Codigo).SingleAsync(ct);
        plataforma.AsignacionesRol.Remove(asignacion);
        plataforma.Auditoria.Add(EventoAsignacion(AccionesAuditoriaRoles.AsignacionRetirada, asignacion, rol, actor));
        await plataforma.SaveChangesAsync(ct);
        await invalidador.InvalidarSesionesAsync(asignacion.PersonaId, MotivosInvalidacion.CambioDeRoles, actor, ct);
        return new Resultado<AsignacionRol>(asignacion, null);
    }

    public async Task<Resultado<MapeoGrupoEntra>> CrearMapeoAsync(Guid grupoId, string grupoNombre, string rol, Guid? actor, CancellationToken ct = default)
    {
        if (grupoId == Guid.Empty)
        {
            return Invalido("grupoId", "grupo_invalido", "El grupo es el object id del grupo en Entra ID.");
        }
        if (string.IsNullOrWhiteSpace(grupoNombre) || grupoNombre.Trim().Length > 200)
        {
            return Invalido("grupoNombre", "nombre_invalido", "El nombre del grupo es obligatorio (hasta 200 caracteres).");
        }
        var definicion = catalogo.Rol(rol ?? string.Empty);
        if (definicion is null || definicion.Tipo != TipoRol.Interno || definicion.Codigo == RolesAtlas.Lector)
        {
            return Invalido("rol", "rol_no_mapeable", "Un grupo de Entra solo otorga roles internos asignables (atlas.lector ya lo tiene todo empleado).");
        }
        var rolId = await plataforma.Roles.Where(r => r.Codigo == definicion.Codigo).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (rolId is null)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "rol_no_sembrado", "El rol aún no está en la base; corre el migrador.");
        }
        if (await plataforma.MapeosGrupoEntra.AnyAsync(m => m.GrupoId == grupoId && m.RolId == rolId, ct))
        {
            return new ErrorAdministracion(TipoError.Conflicto, "mapeo_duplicado", "El grupo ya otorga ese rol.");
        }
        var mapeo = new MapeoGrupoEntra { GrupoId = grupoId, GrupoNombre = grupoNombre.Trim(), RolId = rolId.Value };
        plataforma.MapeosGrupoEntra.Add(mapeo);
        plataforma.Auditoria.Add(Evento(AccionesAuditoriaRoles.MapeoCreado, "mapeo_grupo_entra", mapeo.Id, null, actor,
            new { grupo_id = grupoId, grupo_nombre = mapeo.GrupoNombre, rol = definicion.Codigo }));
        await plataforma.SaveChangesAsync(ct);
        // Las personas del grupo reciben el rol en su siguiente ingreso (los grupos llegan con el token de Entra).
        return new Resultado<MapeoGrupoEntra>(mapeo, null);
    }

    /// <summary>
    /// Elimina un mapeo. Si ningún otro grupo otorga el mismo rol, retira ya las asignaciones de grupo de ese rol e
    /// invalida las sesiones de esas personas; si otro grupo lo otorga, el siguiente ingreso decide.
    /// </summary>
    public async Task<Resultado<MapeoGrupoEntra>> EliminarMapeoAsync(Guid mapeoId, Guid? actor, CancellationToken ct = default)
    {
        var mapeo = await plataforma.MapeosGrupoEntra.FirstOrDefaultAsync(m => m.Id == mapeoId, ct);
        if (mapeo is null)
        {
            return new ErrorAdministracion(TipoError.NoEncontrado, "mapeo_no_encontrado", "El mapeo no existe.");
        }
        var rol = await plataforma.Roles.Where(r => r.Id == mapeo.RolId).Select(r => r.Codigo).SingleAsync(ct);
        plataforma.MapeosGrupoEntra.Remove(mapeo);
        plataforma.Auditoria.Add(Evento(AccionesAuditoriaRoles.MapeoEliminado, "mapeo_grupo_entra", mapeo.Id, null, actor,
            new { grupo_id = mapeo.GrupoId, grupo_nombre = mapeo.GrupoNombre, rol }));

        var otroGrupo = await plataforma.MapeosGrupoEntra.AnyAsync(m => m.RolId == mapeo.RolId && m.Id != mapeo.Id, ct);
        var retiradas = otroGrupo
            ? []
            : await plataforma.AsignacionesRol.Where(a => a.RolId == mapeo.RolId && a.Origen == OrigenAsignacion.GrupoEntra).ToListAsync(ct);
        foreach (var asignacion in retiradas)
        {
            plataforma.AsignacionesRol.Remove(asignacion);
            plataforma.Auditoria.Add(EventoAsignacion(AccionesAuditoriaRoles.AsignacionRetirada, asignacion, rol, actor));
        }
        await plataforma.SaveChangesAsync(ct);
        foreach (var persona in retiradas.Select(a => a.PersonaId).Distinct())
        {
            await invalidador.InvalidarSesionesAsync(persona, MotivosInvalidacion.CambioDeRoles, actor, ct);
        }
        return new Resultado<MapeoGrupoEntra>(mapeo, null);
    }

    private static ErrorAdministracion Invalido(string campo, string codigo, string mensaje) =>
        new(TipoError.Invalido, codigo, mensaje, campo);

    /// <summary>Fila de auditoría de una asignación (también la usan el aprovisionamiento y el vigilante de vencimientos).</summary>
    internal EventoAuditoria EventoAsignacion(string accion, AsignacionRol a, string rol, Guid? actor) =>
        Evento(accion, "asignacion_rol", a.Id, a.OrganizacionId, actor, DatosAsignacion(a, rol));

    internal static object DatosAsignacion(AsignacionRol a, string rol) => new
    {
        persona_id = a.PersonaId,
        rol,
        alcance = new AlcanceAsignacion(a.AlcanceTipo, a.AlcanceId).ToString(),
        vigente_desde = a.VigenteDesde,
        vigente_hasta = a.VigenteHasta,
        origen = ConvertidorOrigen(a.Origen),
    };

    internal EventoAuditoria Evento(string accion, string recursoTipo, Guid recursoId, Guid? organizacion, Guid? actor, object datos) =>
        CrearEvento(reloj.GetUtcNow(), accion, recursoTipo, recursoId, organizacion, actor, datos, RegistroSeguridad.IpCliente(contexto.HttpContext));

    internal static EventoAuditoria CrearEvento(
        DateTimeOffset ahora, string accion, string recursoTipo, Guid recursoId, Guid? organizacion, Guid? actor, object datos,
        System.Net.IPAddress? ip = null) => new()
        {
            OcurridoEn = ahora,
            ActorTipo = actor is null ? TipoActor.Sistema : TipoActor.Persona,
            ActorId = actor?.ToString(),
            OrganizacionId = organizacion,
            Accion = accion,
            RecursoTipo = recursoTipo,
            RecursoId = recursoId.ToString(),
            Ip = ip,
            Datos = JsonSerializer.Serialize(datos),
            CorrelacionId = Activity.Current?.TraceId.ToString(),
        };

    internal static string ConvertidorOrigen(OrigenAsignacion origen) => origen switch
    {
        OrigenAsignacion.GrupoEntra => "grupo_entra",
        OrigenAsignacion.Invitacion => "invitacion",
        _ => "manual",
    };

    [GeneratedRegex("^[^\\s]{1,200}$")]
    private static partial Regex IdAlcance();
}
