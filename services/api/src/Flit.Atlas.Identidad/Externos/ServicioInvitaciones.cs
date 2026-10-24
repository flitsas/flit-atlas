using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>Quién administra: la persona de la sesión, si es externa y su organización.</summary>
public sealed record ActorInvitaciones(Guid PersonaId, bool Externo, Guid OrganizacionId)
{
    public static ActorInvitaciones De(ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return new(
            Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.PersonaId)!),
            usuario.FindFirstValue(ClaimsAtlas.TipoPrincipal) == ClaimsAtlas.TipoExterno,
            Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.OrganizacionId)!));
    }
}

/// <summary>
/// Invitaciones de externos (prompt maestro §6.2, HU 1.5, ATLAS-ADR-0025): crear, reenviar, suspender, renovar y
/// revocar, con el correo que envía Atlas. Quien tiene <c>atlas.invitaciones.gestionar</c> como empleado invita a
/// cualquier organización externa; un <c>ext.admin_organizacion</c> (si su organización lo tiene habilitado) solo a la
/// suya, sin el rol de administrador y dentro del alcance, el vencimiento y los permisos extra que él mismo tiene. El
/// predicado obligatorio y RLS ya le ocultan las demás organizaciones. Cada operación queda en los eventos de seguridad.
/// </summary>
public sealed partial class ServicioInvitaciones(
    PlataformaDbContext plataforma,
    DbContextOptions<PlataformaDbContext> opcionesContexto,
    CatalogoPermisos catalogo,
    AdministracionRoles roles,
    IInvalidadorSesiones invalidador,
    RegistroSeguridad registro,
    CorreosExternos correos,
    IOptions<OpcionesExternos> opciones,
    TimeProvider reloj)
{
    /// <summary>Roles que un administrador de organización puede invitar (nunca otro administrador: no amplía privilegios).</summary>
    public static readonly string[] RolesDeAdminOrganizacion =
        [RolesAtlas.Integrador, RolesAtlas.ClienteFuncional, RolesAtlas.AsesorComercial, RolesAtlas.Proveedor];

    /// <summary>Alcances del núcleo que no van en una invitación: el externo ya queda en su organización.</summary>
    private static readonly string[] AlcancesNoInvitables = [TiposAlcance.Global, TiposAlcance.Organizacion, TiposAlcance.Ambiente];

    public const int MaximoAlcances = 50;

    private OpcionesExternos Opciones => opciones.Value;

    public IReadOnlyList<string> TiposAlcanceInvitables => [.. catalogo.TiposAlcance.Except(AlcancesNoInvitables, StringComparer.Ordinal)];

    public async Task<OpcionesInvitacion> OpcionesAsync(ActorInvitaciones actor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var organizaciones = await plataforma.Organizaciones.AsNoTracking()
            .Where(o => o.Tipo != TipoOrganizacion.Interna && o.Estado == EstadoOrganizacion.Activa)
            .Where(o => !actor.Externo || o.Id == actor.OrganizacionId)
            .OrderBy(o => o.Nombre)
            .Select(o => new OrganizacionInvitable(o.Id, o.Nombre, o.Tipo == TipoOrganizacion.Proveedor ? "proveedor" : "cliente", o.AdminOrganizacionHabilitado))
            .ToListAsync(ct);
        var codigos = actor.Externo ? RolesDeAdminOrganizacion : [.. RolesAtlas.Externos];
        var rolesInvitables = catalogo.Roles.Where(r => codigos.Contains(r.Codigo))
            .Select(r => new RolInvitable(r.Codigo, r.Nombre, r.Descripcion)).ToList();
        return new OpcionesInvitacion(organizaciones, rolesInvitables, TiposAlcanceInvitables, PermisosExtraInvitacion.Todos,
            (int)Opciones.VigenciaMaximaAcceso.TotalDays, actor.Externo);
    }

    public async Task<IReadOnlyList<InvitacionVista>> ListarAsync(Guid? organizacionId, CancellationToken ct)
    {
        var fuente = plataforma.Invitaciones.AsNoTracking().Where(i => organizacionId == null || i.OrganizacionId == organizacionId);
        var lista = await fuente.OrderByDescending(i => i.CreadoEn).Take(500).ToListAsync(ct);
        return await VistasAsync(lista, ct);
    }

    public async Task<Resultado<InvitacionEnviada>> CrearAsync(SolicitudInvitacion solicitud, ActorInvitaciones actor, HttpContext? http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ArgumentNullException.ThrowIfNull(actor);
        var correo = (solicitud.Correo ?? string.Empty).Trim().ToLowerInvariant();
        if (correo.Length is 0 or > 320 || !MailAddress.TryCreate(correo, out var direccion) || direccion.Address != correo || !correo.Contains('.', StringComparison.Ordinal))
        {
            return Invalido("correo", "correo_invalido", "Escribe un correo válido.");
        }
        var nombre = string.IsNullOrWhiteSpace(solicitud.Nombre) ? null : solicitud.Nombre.Trim();
        if (nombre is { Length: > 200 })
        {
            return Invalido("nombre", "nombre_invalido", "El nombre tiene hasta 200 caracteres.");
        }

        var organizacion = await plataforma.Organizaciones.FirstOrDefaultAsync(o => o.Id == solicitud.OrganizacionId, ct);
        if (organizacion is null || (actor.Externo && organizacion.Id != actor.OrganizacionId))
        {
            return Invalido("organizacionId", "organizacion_no_encontrada", "La organización no existe.");
        }
        if (organizacion.Tipo == TipoOrganizacion.Interna)
        {
            return Invalido("organizacionId", "organizacion_interna", "Los empleados de FLIT entran con Microsoft: no se invitan.");
        }
        if (organizacion.Estado != EstadoOrganizacion.Activa)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "organizacion_suspendida", "La organización está suspendida.");
        }

        var definicion = catalogo.Rol(solicitud.Rol ?? string.Empty);
        if (definicion is null || definicion.Tipo != TipoRol.Externo)
        {
            return Invalido("rol", "rol_no_invitable", "Una invitación lleva un rol externo (ext.*).");
        }
        if (definicion.Codigo == RolesAtlas.AdminOrganizacion && !organizacion.AdminOrganizacionHabilitado)
        {
            return Invalido("rol", "admin_organizacion_no_habilitado", "Esta organización no tiene habilitado el rol de administrador de la organización.");
        }

        var alcance = NormalizarAlcance(solicitud.Alcance);
        if (alcance.Error is { } errorAlcance)
        {
            return errorAlcance;
        }
        var extra = (solicitud.PermisosExtra ?? []).Select(p => p.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (extra.Any(p => !PermisosExtraInvitacion.Todos.Contains(p)))
        {
            return Invalido("permisosExtra", "permiso_extra_desconocido", $"Permisos extra válidos: {string.Join(", ", PermisosExtraInvitacion.Todos)}.");
        }

        var ahora = reloj.GetUtcNow();
        var proveedor = definicion.Codigo == RolesAtlas.Proveedor;
        if (proveedor && (solicitud.AccesoVenceEn is null || alcance.Lista.Count == 0))
        {
            return Invalido(solicitud.AccesoVenceEn is null ? "accesoVenceEn" : "alcance", "proveedor_sin_vencimiento_o_alcance",
                "Un proveedor solo ve lo concedido explícitamente: indica el vencimiento y al menos un aplicativo, paquete o documento.");
        }
        var vence = solicitud.AccesoVenceEn ?? ahora + Opciones.VigenciaMaximaAcceso;
        if (ErrorVencimiento(vence, ahora) is { } errorVence)
        {
            return errorVence;
        }

        if (actor.Externo && await ProhibidoParaAdminAsync(actor, definicion.Codigo, alcance.Lista, vence, extra, ahora, ct) is { } prohibido)
        {
            return prohibido;
        }

        // El correo no puede ser de otra persona de Atlas ni tener otra invitación viva. Se revisa con el alcance del
        // sistema: un administrador de organización no ve las demás, pero el correo tampoco puede chocar con ellas.
        await using (var sistema = new PlataformaDbContext(opcionesContexto))
        {
            if (await sistema.Personas.AnyAsync(p => p.Correo == correo, ct))
            {
                return new ErrorAdministracion(TipoError.Conflicto, "correo_no_disponible",
                    "Ese correo ya tiene una cuenta en Atlas: renueva su invitación en lugar de crear otra.");
            }
            if (await sistema.Invitaciones.AnyAsync(i => i.Correo == correo
                    && (i.Estado == EstadoInvitacion.Pendiente || i.Estado == EstadoInvitacion.Suspendida || i.Estado == EstadoInvitacion.Aceptada), ct))
            {
                return new ErrorAdministracion(TipoError.Conflicto, "invitacion_existente",
                    "Ese correo ya tiene una invitación vigente: reenvíala, renuévala o revócala.");
            }
        }

        var rolId = await plataforma.Roles.Where(r => r.Codigo == definicion.Codigo).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (rolId is null)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "rol_no_sembrado", "El rol aún no está en la base; corre el migrador.");
        }

        var (token, hash) = TokensEnlace.Generar();
        var invitacion = new Invitacion
        {
            OrganizacionId = organizacion.Id,
            Correo = correo,
            Nombre = nombre,
            RolId = rolId.Value,
            Alcance = JsonSerializer.Serialize(alcance.Lista),
            PermisosExtra = JsonSerializer.Serialize(extra),
            AccesoVenceEn = vence,
            TokenHash = hash,
            TokenVenceEn = ahora + Opciones.VigenciaEnlace,
            InvitadoPor = actor.PersonaId,
        };
        plataforma.Invitaciones.Add(invitacion);
        await plataforma.SaveChangesAsync(ct);
        await RegistrarAsync(TiposEventoSeguridad.InvitacionCreada, invitacion, actor.PersonaId, new
        {
            invitacion_id = invitacion.Id,
            rol = definicion.Codigo,
            alcance = alcance.Lista.Select(a => $"{a.Tipo}:{a.Id}").ToList(),
            permisos_extra = extra,
            acceso_vence_en = vence,
        }, ct);

        var enviado = await EnviarAsync(invitacion, token, organizacion.Nombre, definicion.Nombre, actor.PersonaId, http, ct);
        return new Resultado<InvitacionEnviada>(new InvitacionEnviada((await VistasAsync([invitacion], ct))[0], enviado), null);
    }

    /// <summary>Enlace nuevo de 72 h (el anterior deja de servir) y correo otra vez. Solo para invitaciones pendientes.</summary>
    public async Task<Resultado<InvitacionEnviada>> ReenviarAsync(Guid id, ActorInvitaciones actor, HttpContext? http, CancellationToken ct)
    {
        var (invitacion, error) = await BuscarParaActuarAsync(id, actor, ct);
        if (error is not null)
        {
            return error;
        }
        if (invitacion!.Estado != EstadoInvitacion.Pendiente)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "estado_no_permite",
                "Solo se reenvía una invitación pendiente. Una suspendida se renueva; una aceptada o revocada, no.");
        }
        var ahora = reloj.GetUtcNow();
        if (invitacion.AccesoVenceEn <= ahora)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "acceso_vencido", "El acceso de la invitación ya venció: renuévala con un vencimiento nuevo.");
        }
        var token = NuevoEnlace(invitacion, ahora);
        await plataforma.SaveChangesAsync(ct);
        await RegistrarAsync(TiposEventoSeguridad.InvitacionReenviada, invitacion, actor.PersonaId, new { invitacion_id = invitacion.Id, envio = invitacion.Envios + 1 }, ct);
        var enviado = await EnviarAsync(invitacion, token, null, null, actor.PersonaId, http, ct);
        return new Resultado<InvitacionEnviada>(new InvitacionEnviada((await VistasAsync([invitacion], ct))[0], enviado), null);
    }

    /// <summary>
    /// Pendiente: el enlace deja de servir. Aceptada: la persona queda suspendida y sus sesiones se cierran de inmediato.
    /// Se deshace con <see cref="RenovarAsync"/>.
    /// </summary>
    public async Task<Resultado<InvitacionVista>> SuspenderAsync(Guid id, ActorInvitaciones actor, CancellationToken ct)
    {
        var (invitacion, error) = await BuscarParaActuarAsync(id, actor, ct);
        if (error is not null)
        {
            return error;
        }
        if (invitacion!.Estado is not (EstadoInvitacion.Pendiente or EstadoInvitacion.Aceptada))
        {
            return new ErrorAdministracion(TipoError.Conflicto, "estado_no_permite", "Solo se suspende una invitación pendiente o aceptada.");
        }
        var aceptada = invitacion.Estado == EstadoInvitacion.Aceptada;
        invitacion.Estado = EstadoInvitacion.Suspendida;
        invitacion.EstadoCambiadoEn = reloj.GetUtcNow();
        invitacion.EstadoCambiadoPor = actor.PersonaId;
        if (aceptada && invitacion.PersonaId is { } personaId)
        {
            // Primero el estado y después el sello (ATLAS-ADR-0022).
            var persona = await plataforma.Personas.SingleAsync(p => p.Id == personaId, ct);
            persona.Estado = EstadoPersona.Suspendida;
        }
        await plataforma.SaveChangesAsync(ct);
        await RegistrarAsync(TiposEventoSeguridad.InvitacionSuspendida, invitacion, actor.PersonaId, new { invitacion_id = invitacion.Id, aceptada }, ct);
        if (aceptada && invitacion.PersonaId is { } suspendida)
        {
            await invalidador.InvalidarSesionesAsync(suspendida, MotivosInvalidacion.Suspension, actor.PersonaId, ct);
        }
        return new Resultado<InvitacionVista>((await VistasAsync([invitacion], ct))[0], null);
    }

    /// <summary>
    /// Vencimiento nuevo del acceso. Aceptada (o suspendida después de aceptar): extiende las asignaciones de la invitación
    /// y reactiva a la persona. Sin aceptar: vuelve a pendiente con un enlace nuevo de 72 h.
    /// </summary>
    public async Task<Resultado<InvitacionEnviada>> RenovarAsync(
        Guid id, SolicitudRenovacion solicitud, ActorInvitaciones actor, HttpContext? http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var (invitacion, error) = await BuscarParaActuarAsync(id, actor, ct);
        if (error is not null)
        {
            return error;
        }
        if (invitacion!.Estado == EstadoInvitacion.Revocada)
        {
            return new ErrorAdministracion(TipoError.Conflicto, "estado_no_permite", "Una invitación revocada no se renueva: crea otra.");
        }
        var ahora = reloj.GetUtcNow();
        var vence = solicitud.AccesoVenceEn ?? ahora + Opciones.VigenciaMaximaAcceso;
        if (ErrorVencimiento(vence, ahora) is { } errorVence)
        {
            return errorVence;
        }
        var rol = await plataforma.Roles.Where(r => r.Id == invitacion.RolId).Select(r => r.Codigo).SingleAsync(ct);
        var alcance = JsonSerializer.Deserialize<List<EndpointsRoles.AlcanceVista>>(invitacion.Alcance) ?? [];
        var extra = JsonSerializer.Deserialize<List<string>>(invitacion.PermisosExtra) ?? [];
        if (actor.Externo && await ProhibidoParaAdminAsync(actor, rol, alcance, vence, extra, ahora, ct) is { } prohibido)
        {
            return prohibido;
        }
        if (actor.Externo && await SuspensionAjenaAsync(invitacion, actor, ct))
        {
            return new ErrorAdministracion(TipoError.Prohibido, "suspension_de_flit",
                "Esta persona la suspendió FLIT: solo FLIT puede reactivarla.");
        }

        invitacion.AccesoVenceEn = vence;
        invitacion.EstadoCambiadoEn = ahora;
        invitacion.EstadoCambiadoPor = actor.PersonaId;
        string? token = null;
        var asignacionesRenovadas = 0;
        if (invitacion.PersonaId is { } personaId)
        {
            invitacion.Estado = EstadoInvitacion.Aceptada;
            var persona = await plataforma.Personas.SingleAsync(p => p.Id == personaId, ct);
            if (persona.Estado == EstadoPersona.Suspendida)
            {
                persona.Estado = EstadoPersona.Activa;
            }
            var asignaciones = await plataforma.AsignacionesRol
                .Where(a => a.PersonaId == personaId && a.Origen == OrigenAsignacion.Invitacion).ToListAsync(ct);
            foreach (var asignacion in asignaciones)
            {
                asignacion.VigenteHasta = vence;
                asignacion.VencimientoProcesadoEn = null;
                plataforma.Auditoria.Add(roles.EventoAsignacion(AccionAsignacionRenovada, asignacion, rol, actor.PersonaId));
            }
            asignacionesRenovadas = asignaciones.Count;
        }
        else
        {
            invitacion.Estado = EstadoInvitacion.Pendiente;
            token = NuevoEnlace(invitacion, ahora);
        }
        await plataforma.SaveChangesAsync(ct);
        await RegistrarAsync(TiposEventoSeguridad.InvitacionRenovada, invitacion, actor.PersonaId,
            new { invitacion_id = invitacion.Id, acceso_vence_en = vence, asignaciones = asignacionesRenovadas, enlace_nuevo = token is not null }, ct);
        var enviado = token is not null && await EnviarAsync(invitacion, token, null, null, actor.PersonaId, http, ct);
        return new Resultado<InvitacionEnviada>(new InvitacionEnviada((await VistasAsync([invitacion], ct))[0], enviado), null);
    }

    public const string AccionAsignacionRenovada = "plataforma.asignacion_renovada";

    /// <summary>
    /// ¿Renovar desharía una suspensión que no hizo la organización del administrador externo (F4-SEC-14)? La invitación
    /// suspendida por alguien de otra organización (FLIT), o la persona suspendida desde la administración de FLIT.
    /// </summary>
    private async Task<bool> SuspensionAjenaAsync(Invitacion invitacion, ActorInvitaciones actor, CancellationToken ct)
    {
        if (invitacion.Estado == EstadoInvitacion.Suspendida)
        {
            var suspendio = invitacion.EstadoCambiadoPor;
            return suspendio is null || !await plataforma.Personas.AnyAsync(p => p.Id == suspendio && p.OrganizacionId == actor.OrganizacionId, ct);
        }
        return invitacion.PersonaId is { } personaId
            && await plataforma.Personas.AnyAsync(p => p.Id == personaId && p.Estado == EstadoPersona.Suspendida, ct);
    }

    /// <summary>Definitivo. Si se había aceptado, retira las asignaciones de la invitación (eso cierra las sesiones).</summary>
    public async Task<Resultado<InvitacionVista>> RevocarAsync(Guid id, ActorInvitaciones actor, CancellationToken ct)
    {
        var (invitacion, error) = await BuscarParaActuarAsync(id, actor, ct);
        if (error is not null)
        {
            return error;
        }
        if (invitacion!.Estado == EstadoInvitacion.Revocada)
        {
            return new Resultado<InvitacionVista>((await VistasAsync([invitacion], ct))[0], null);
        }
        invitacion.Estado = EstadoInvitacion.Revocada;
        invitacion.EstadoCambiadoEn = reloj.GetUtcNow();
        invitacion.EstadoCambiadoPor = actor.PersonaId;
        await plataforma.SaveChangesAsync(ct);
        var retiradas = 0;
        if (invitacion.PersonaId is { } personaId)
        {
            var asignaciones = await plataforma.AsignacionesRol
                .Where(a => a.PersonaId == personaId && a.Origen == OrigenAsignacion.Invitacion).Select(a => a.Id).ToListAsync(ct);
            foreach (var asignacion in asignaciones)
            {
                if ((await roles.RetirarAsync(asignacion, actor.PersonaId, ct)).Error is null)
                {
                    retiradas++;
                }
            }
            // Retirar cada asignación ya cerró sus sesiones; si no tenía ninguna, se cierran igual.
            if (retiradas == 0)
            {
                await invalidador.InvalidarSesionesAsync(personaId, MotivosInvalidacion.CambioDeRoles, actor.PersonaId, ct);
            }
        }
        await RegistrarAsync(TiposEventoSeguridad.InvitacionRevocada, invitacion, actor.PersonaId, new { invitacion_id = invitacion.Id, asignaciones_retiradas = retiradas }, ct);
        return new Resultado<InvitacionVista>((await VistasAsync([invitacion], ct))[0], null);
    }

    /// <summary>La invitación (visible para la sesión) y si el actor puede tocarla.</summary>
    private async Task<(Invitacion? Invitacion, ErrorAdministracion? Error)> BuscarParaActuarAsync(Guid id, ActorInvitaciones actor, CancellationToken ct)
    {
        var invitacion = await plataforma.Invitaciones.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invitacion is null)
        {
            return (null, new ErrorAdministracion(TipoError.NoEncontrado, "invitacion_no_encontrada", "La invitación no existe."));
        }
        if (actor.Externo)
        {
            var rol = await plataforma.Roles.Where(r => r.Id == invitacion.RolId).Select(r => r.Codigo).SingleAsync(ct);
            if (invitacion.OrganizacionId != actor.OrganizacionId || !RolesDeAdminOrganizacion.Contains(rol) || invitacion.PersonaId == actor.PersonaId)
            {
                return (null, new ErrorAdministracion(TipoError.Prohibido, "fuera_de_tu_alcance",
                    "Como administrador de tu organización solo gestionas las invitaciones de tus colegas que tú podrías crear."));
            }
        }
        return (invitacion, null);
    }

    /// <summary>
    /// Un administrador de organización no amplía alcances (§7.3): solo roles no administrativos, alcance dentro del suyo,
    /// vencimiento no posterior al suyo y permisos extra que él tenga.
    /// </summary>
    private async Task<ErrorAdministracion?> ProhibidoParaAdminAsync(
        ActorInvitaciones actor, string rol, List<EndpointsRoles.AlcanceVista> alcance, DateTimeOffset vence,
        List<string> extra, DateTimeOffset ahora, CancellationToken ct)
    {
        if (!RolesDeAdminOrganizacion.Contains(rol))
        {
            return Prohibido("Como administrador de tu organización no puedes invitar a otro administrador.");
        }
        var propias = await plataforma.AsignacionesRol.AsNoTracking()
            .Where(a => a.PersonaId == actor.PersonaId && a.VigenteDesde <= ahora && (a.VigenteHasta == null || a.VigenteHasta > ahora))
            .Select(a => new { a.AlcanceTipo, a.AlcanceId, a.VigenteHasta })
            .ToListAsync(ct);
        if (propias.Count == 0)
        {
            return Prohibido("No tienes un acceso vigente.");
        }
        var global = propias.Any(a => a.AlcanceTipo == TiposAlcance.Global);
        if (!global && (alcance.Count == 0 || alcance.Any(x => !propias.Any(p => p.AlcanceTipo == x.Tipo && p.AlcanceId == x.Id))))
        {
            return Prohibido("Solo puedes conceder los aplicativos, paquetes o documentos que ya tiene tu organización contigo.");
        }
        var tuVencimiento = propias.Max(a => a.VigenteHasta ?? DateTimeOffset.MaxValue);
        if (vence > tuVencimiento)
        {
            return Prohibido($"El acceso no puede vencer después del tuyo ({tuVencimiento.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}).");
        }
        if (extra.Count > 0)
        {
            var propiosExtra = await plataforma.Invitaciones.AsNoTracking()
                .Where(i => i.PersonaId == actor.PersonaId && i.Estado == EstadoInvitacion.Aceptada)
                .Select(i => i.PermisosExtra).ToListAsync(ct);
            var tienes = propiosExtra.SelectMany(j => JsonSerializer.Deserialize<List<string>>(j) ?? []).ToHashSet(StringComparer.Ordinal);
            if (extra.Any(e => !tienes.Contains(e)))
            {
                return Prohibido("Solo puedes conceder los permisos extra que tú tienes.");
            }
        }
        return null;
    }

    private static ErrorAdministracion Prohibido(string mensaje) => new(TipoError.Prohibido, "fuera_de_tu_alcance", mensaje);

    private ErrorAdministracion? ErrorVencimiento(DateTimeOffset vence, DateTimeOffset ahora)
    {
        if (vence <= ahora + TimeSpan.FromMinutes(1))
        {
            return Invalido("accesoVenceEn", "vencimiento_invalido", "El vencimiento del acceso debe ser posterior a la hora actual.");
        }
        if (vence > ahora + Opciones.VigenciaMaximaAcceso + TimeSpan.FromMinutes(5))
        {
            return Invalido("accesoVenceEn", "vencimiento_excede_maximo",
                $"El acceso de un externo vence a más tardar a los {Opciones.VigenciaMaximaAcceso.TotalDays:0} días.");
        }
        return null;
    }

    private (List<EndpointsRoles.AlcanceVista> Lista, ErrorAdministracion? Error) NormalizarAlcance(IReadOnlyList<EndpointsRoles.AlcanceVista>? alcance)
    {
        var lista = new List<EndpointsRoles.AlcanceVista>();
        var tipos = TiposAlcanceInvitables;
        foreach (var item in alcance ?? [])
        {
            var tipo = item.Tipo?.Trim() ?? string.Empty;
            var idRecurso = item.Id?.Trim() ?? string.Empty;
            if (!tipos.Contains(tipo) || !IdAlcance().IsMatch(idRecurso))
            {
                return (lista, Invalido("alcance", "alcance_invalido",
                    $"Cada alcance lleva un tipo ({string.Join(", ", tipos)}) y el id del recurso (hasta 200 caracteres, sin espacios)."));
            }
            if (!lista.Any(a => a.Tipo == tipo && a.Id == idRecurso))
            {
                lista.Add(new EndpointsRoles.AlcanceVista(tipo, idRecurso));
            }
        }
        return lista.Count > MaximoAlcances
            ? (lista, Invalido("alcance", "alcance_invalido", $"Hasta {MaximoAlcances} recursos por invitación."))
            : (lista, null);
    }

    private string NuevoEnlace(Invitacion invitacion, DateTimeOffset ahora)
    {
        var (token, hash) = TokensEnlace.Generar();
        invitacion.TokenHash = hash;
        invitacion.TokenVenceEn = ahora + Opciones.VigenciaEnlace;
        return token;
    }

    /// <summary>Envía el correo después de guardar: si falla, la invitación queda y se puede reenviar.</summary>
    private async Task<bool> EnviarAsync(
        Invitacion invitacion, string token, string? organizacion, string? rol, Guid actor, HttpContext? http, CancellationToken ct)
    {
        invitacion.Envios++;
        organizacion ??= await plataforma.Organizaciones.Where(o => o.Id == invitacion.OrganizacionId).Select(o => o.Nombre).SingleAsync(ct);
        rol ??= await plataforma.Roles.Where(r => r.Id == invitacion.RolId).Select(r => r.Nombre).SingleAsync(ct);
        var enviado = await correos.EnviarInvitacionAsync(invitacion, token, organizacion, rol, http, ct);
        if (enviado)
        {
            invitacion.EnviadaEn = reloj.GetUtcNow();
        }
        else
        {
            invitacion.EnviadaEn = null;
            await RegistrarAsync(TiposEventoSeguridad.CorreoNoEnviado, invitacion, actor, new { invitacion_id = invitacion.Id, motivo = "invitacion" }, ct);
        }
        await plataforma.SaveChangesAsync(ct);
        return enviado;
    }

    private Task RegistrarAsync(string tipo, Invitacion invitacion, Guid actor, object detalle, CancellationToken ct) =>
        registro.RegistrarAsync(tipo, new DatosEventoSeguridad
        {
            PersonaId = invitacion.PersonaId,
            ActorPersonaId = actor,
            OrganizacionId = invitacion.OrganizacionId,
            RecursoTipo = "invitacion",
            RecursoId = invitacion.Id,
            Detalle = detalle,
        }, ct);

    /// <summary>Estado calculado en este momento: los vencimientos no dependen de un proceso que los marque.</summary>
    public static string Estado(Invitacion invitacion, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(invitacion);
        return invitacion.Estado switch
        {
            EstadoInvitacion.Revocada => EstadosInvitacion.Revocada,
            EstadoInvitacion.Suspendida => EstadosInvitacion.Suspendida,
            EstadoInvitacion.Aceptada => invitacion.AccesoVenceEn <= ahora ? EstadosInvitacion.AccesoVencido : EstadosInvitacion.Aceptada,
            _ => invitacion.AccesoVenceEn <= ahora ? EstadosInvitacion.AccesoVencido
                : invitacion.TokenVenceEn <= ahora ? EstadosInvitacion.EnlaceVencido : EstadosInvitacion.Pendiente,
        };
    }

    internal async Task<IReadOnlyList<InvitacionVista>> VistasAsync(IReadOnlyList<Invitacion> invitaciones, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var organizaciones = invitaciones.Select(i => i.OrganizacionId).Distinct().ToList();
        var nombres = await plataforma.Organizaciones.AsNoTracking().Where(o => organizaciones.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Nombre, ct);
        var rolesIds = invitaciones.Select(i => i.RolId).Distinct().ToList();
        var rolesVista = await plataforma.Roles.AsNoTracking().Where(r => rolesIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => new { r.Codigo, r.Nombre }, ct);
        return
        [
            .. invitaciones.Select(i => new InvitacionVista(
                i.Id, i.Correo, i.Nombre, i.OrganizacionId, nombres.GetValueOrDefault(i.OrganizacionId, "?"),
                rolesVista.GetValueOrDefault(i.RolId)?.Codigo ?? "?", rolesVista.GetValueOrDefault(i.RolId)?.Nombre ?? "?",
                JsonSerializer.Deserialize<List<EndpointsRoles.AlcanceVista>>(i.Alcance) ?? [],
                JsonSerializer.Deserialize<List<string>>(i.PermisosExtra) ?? [],
                i.AccesoVenceEn, i.TokenVenceEn, Estado(i, ahora), i.PersonaId, i.AceptadaEn, i.EnviadaEn, i.Envios, i.CreadoEn)),
        ];
    }

    private static ErrorAdministracion Invalido(string campo, string codigo, string mensaje) => new(TipoError.Invalido, codigo, mensaje, campo);

    [GeneratedRegex("^[^\\s]{1,200}$")]
    private static partial Regex IdAlcance();
}
