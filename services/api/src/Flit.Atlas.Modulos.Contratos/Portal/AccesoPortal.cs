using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Contratos.EndpointsContratos;

namespace Flit.Atlas.Modulos.Contratos.Portal;

/// <summary>Qué ve una persona de un paquete (o de un contrato del paquete) en el portal (prompt maestro §7.3, ATLAS-ADR-0043).</summary>
internal enum NivelPortal
{
    /// <summary>No lo ve.</summary>
    Ninguno = 0,

    /// <summary>Nombre, descripción, contratos y versiones, y avisos de deprecación: sin operaciones, spec, guías ni cambios.</summary>
    Resumen = 1,

    /// <summary>Todo lo compartido: operaciones, spec podado, guías y cambios.</summary>
    Tecnico = 2,
}

/// <summary>
/// Alcance de los roles en el portal (prompt maestro §7.3, ATLAS-ADR-0043). Un rol que recibe <c>atlas.portal.ver</c> y no
/// está en esta tabla no ve nada (falla cerrada): <c>AislamientoPortalTests</c> exige que cada uno tenga su fila.
/// </summary>
internal static class AlcanceRolesPortal
{
    /// <summary>Cómo cubre una asignación a un paquete: por toda la organización o porque la invitación lo nombra.</summary>
    public enum Cobertura
    {
        /// <summary>Alcance global u organización: todo lo que se comparta con la organización.</summary>
        Organizacion,

        /// <summary>Alcance <c>paquete:&lt;id&gt;</c>: la invitación nombra el paquete.</summary>
        Paquete,

        /// <summary>Alcance <c>aplicativo:&lt;id&gt;</c>: los contratos de ese aplicativo (o de sus hijos) en el paquete.</summary>
        Aplicativo,
    }

    /// <summary>
    /// El nivel que da un rol según cómo lo cubre su asignación. Los internos con <c>atlas.portal.ver</c> (líder técnico,
    /// soporte, superadministrador) ven todo lo de su organización: FLIT no tiene comparticiones, así que solo les sirve para
    /// probar el portal con una cuenta de prueba.
    /// </summary>
    public static NivelPortal Nivel(string rol, TipoRol? tipo, Cobertura cobertura) => rol switch
    {
        // Sistema y equipo técnico del cliente: paquetes compartidos con spec podado, guías, cambios y avisos.
        RolesAtlas.Integrador => NivelPortal.Tecnico,
        // Solo lo concedido explícitamente (§7.3): con alcance de toda la organización no ve nada.
        RolesAtlas.Proveedor => cobertura == Cobertura.Organizacion ? NivelPortal.Ninguno : NivelPortal.Tecnico,
        // Sin contratos técnicos «salvo que se compartan» con la persona: el detalle, solo si su invitación nombra el paquete.
        RolesAtlas.ClienteFuncional => cobertura == Cobertura.Paquete ? NivelPortal.Tecnico : NivelPortal.Resumen,
        // Fichas comerciales y funcionales (F5); de los contratos, sin detalle técnico.
        RolesAtlas.AsesorComercial => NivelPortal.Resumen,
        // Ve qué tiene su organización para invitar colegas dentro de ese alcance; el detalle lo da otro rol.
        RolesAtlas.AdminOrganizacion => NivelPortal.Resumen,
        _ => tipo == TipoRol.Interno ? NivelPortal.Tecnico : NivelPortal.Ninguno,
    };

    /// <summary>Roles externos que la tabla resuelve a propósito (para la prueba que la obliga a estar completa).</summary>
    public static IReadOnlyList<string> Externos { get; } =
        [RolesAtlas.Integrador, RolesAtlas.Proveedor, RolesAtlas.ClienteFuncional, RolesAtlas.AsesorComercial, RolesAtlas.AdminOrganizacion];
}

/// <summary>Una guía del paquete tal como la ve el portal: la revisión que fijó el paquete (aprobada por el responsable).</summary>
internal sealed record GuiaDelPortal(Guid GuiaId, int Revision, string Titulo, string? Operacion);

/// <summary>Un contrato de un paquete que la persona ve, con su estado de hoy.</summary>
/// <param name="Disponible">Falso si su versión se retiró: queda el aviso, sin spec ni operaciones.</param>
internal sealed record ContratoDelPortal(
    EntradaPaquete Entrada, Contrato Contrato, VersionContratoSinContenido Version, IReadOnlyList<GuiaDelPortal> Guias, NivelPortal Nivel)
{
    public bool Disponible => Version.Estado != EstadoVersion.Retirada;

    public bool Tecnico => Nivel == NivelPortal.Tecnico && Disponible;
}

/// <summary>Un paquete compartido con la organización de la persona, ya recortado a su alcance.</summary>
/// <param name="Revision">La que ve la organización: la fijada en la compartición o la vigente del paquete.</param>
internal sealed record PaqueteDelPortal(
    ComparticionPaquete Comparticion, PaqueteContratos Paquete, RevisionPaquete RevisionPaquete, NivelPortal Nivel,
    IReadOnlyList<ContratoDelPortal> Contratos, bool PuedeExportar, bool PuedeSandbox)
{
    public int Revision => RevisionPaquete.Numero;
}

/// <summary>Lo que la persona de la sesión ve en el portal ahora mismo.</summary>
internal sealed record VistaPortal(Guid OrganizacionId, Guid PersonaId, IReadOnlyList<PaqueteDelPortal> Paquetes)
{
    public static VistaPortal Vacia(Guid organizacion, Guid persona) => new(organizacion, persona, []);
}

/// <summary>Por qué una acción del portal (exportar, sandbox) no se permite; <c>null</c> si se permite.</summary>
/// <param name="Codigo"><c>no_encontrado</c> (el paquete no está compartido con su organización o fuera de su alcance),
/// <c>solo_resumen</c> (su rol no ve el detalle técnico), <c>no_permitido_por_comparticion</c> (la compartición no lo
/// permite) o <c>sin_permiso</c> (su rol no lo tiene ni su invitación lo concede).</param>
internal sealed record DecisionPortal(bool Permitida, string? Codigo, PaqueteDelPortal? Paquete)
{
    public static DecisionPortal Si(PaqueteDelPortal paquete) => new(true, null, paquete);

    public static DecisionPortal No(string codigo, PaqueteDelPortal? paquete = null) => new(false, codigo, paquete);
}

/// <summary>
/// El acceso al portal de clientes (HU 4.3 y 4.4, plan 00 §6.2, ATLAS-ADR-0043), calculado en el servidor en cada petición
/// y con la hora actual, como los permisos (ATLAS-ADR-0023):
/// <list type="number">
/// <item>Solo las comparticiones <b>de la organización de la sesión</b>, vigentes y no revocadas (además del predicado
/// obligatorio y RLS de <c>contratos_comparticion</c>, que ya ocultan las de otras organizaciones a un externo).</item>
/// <item>Solo paquetes publicados (ni borradores ni archivados) y su revisión fijada o vigente.</item>
/// <item>Solo lo que cubre el alcance de cada asignación de la persona con <c>atlas.portal.ver</c> (toda la organización,
/// el paquete o los contratos de un aplicativo) y en el nivel que da su rol (<see cref="AlcanceRolesPortal"/>).</item>
/// <item>Solo contratos que siguen compartibles y guías que siguen compartibles y publicadas.</item>
/// </list>
/// Vencida o revocada la compartición, o vencida la asignación, el paquete deja de aparecer en la siguiente petición sin
/// intervención. Es la única puerta del portal a los paquetes: <see cref="EndpointsPortal"/> y las exportaciones (HU 4.5)
/// parten de aquí.
/// </summary>
internal sealed class AccesoPortal(
    ContratosDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion, CatalogoPermisos catalogo,
    ILectorAplicativos lector, TimeProvider reloj)
{
    public const string ExtraExportar = "exportar";
    public const string ExtraSandbox = "sandbox";

    private VistaPortal? _vista;

    /// <summary>Lo que ve la persona de la sesión (se calcula una vez por petición).</summary>
    public async Task<VistaPortal> VistaAsync(ClaimsPrincipal usuario, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return _vista ??= await CalcularAsync(usuario, ct).ConfigureAwait(false);
    }

    /// <summary>Un paquete de la vista, o <c>null</c> si no está compartido con la organización o queda fuera del alcance.</summary>
    public async Task<PaqueteDelPortal?> PaqueteAsync(ClaimsPrincipal usuario, Guid paqueteId, CancellationToken ct = default) =>
        (await VistaAsync(usuario, ct).ConfigureAwait(false)).Paquetes.FirstOrDefault(p => p.Paquete.Id == paqueteId);

    /// <summary>
    /// ¿Esta persona puede exportar este paquete? (HU 4.5): el paquete está en su vista con nivel técnico, la compartición
    /// vigente tiene <c>puede_exportar</c> y la persona tiene <c>atlas.exportaciones.solicitar</c> con un alcance que cubre el
    /// paquete o el permiso extra <c>exportar</c> de su invitación aceptada y vigente.
    /// </summary>
    public async Task<DecisionPortal> PuedeExportarAsync(ClaimsPrincipal usuario, Guid paqueteId, CancellationToken ct = default) =>
        Decidir(await PaqueteAsync(usuario, paqueteId, ct).ConfigureAwait(false), p => p.Comparticion.PuedeExportar, p => p.PuedeExportar);

    /// <summary>¿Esta persona puede usar el sandbox de este paquete? (HU 4.7): lo mismo con <c>puede_sandbox</c> y <c>atlas.sandbox.usar</c>.</summary>
    public async Task<DecisionPortal> PuedeUsarSandboxAsync(ClaimsPrincipal usuario, Guid paqueteId, CancellationToken ct = default) =>
        Decidir(await PaqueteAsync(usuario, paqueteId, ct).ConfigureAwait(false), p => p.Comparticion.PuedeSandbox, p => p.PuedeSandbox);

    private static DecisionPortal Decidir(PaqueteDelPortal? paquete, Func<PaqueteDelPortal, bool> comparticion, Func<PaqueteDelPortal, bool> final)
    {
        if (paquete is null)
        {
            return DecisionPortal.No("no_encontrado");
        }
        if (paquete.Nivel != NivelPortal.Tecnico)
        {
            return DecisionPortal.No("solo_resumen", paquete);
        }
        if (!comparticion(paquete))
        {
            return DecisionPortal.No("no_permitido_por_comparticion", paquete);
        }
        return final(paquete) ? DecisionPortal.Si(paquete) : DecisionPortal.No("sin_permiso", paquete);
    }

    private async Task<VistaPortal> CalcularAsync(ClaimsPrincipal usuario, CancellationToken ct)
    {
        var persona = Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimPersona), out var p) ? p : Guid.Empty;
        var organizacion = Guid.TryParse(usuario.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var o) ? o : Guid.Empty;
        if (persona == Guid.Empty || organizacion == Guid.Empty)
        {
            return VistaPortal.Vacia(organizacion, persona);
        }
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct).ConfigureAwait(false);
        if (!permisos.Tiene(PermisosContratos.PortalVer))
        {
            return VistaPortal.Vacia(organizacion, persona);
        }

        var ahora = reloj.GetUtcNow();
        // Siempre la organización de la sesión, aunque el alcance de datos sea total (un empleado).
        var comparticiones = await contexto.Comparticiones.AsNoTracking()
            .Where(c => c.OrganizacionId == organizacion && c.RevocadoEn == null && c.VigenteDesde <= ahora && (c.VigenteHasta == null || c.VigenteHasta > ahora))
            .ToListAsync(ct).ConfigureAwait(false);
        if (comparticiones.Count == 0)
        {
            return VistaPortal.Vacia(organizacion, persona);
        }
        var idsPaquetes = comparticiones.Select(c => c.PaqueteId).ToList();
        var paquetes = await contexto.Paquetes.AsNoTracking()
            .Where(x => idsPaquetes.Contains(x.Id) && x.Estado == EstadoPaquete.Publicado && x.RevisionPublicada != null)
            .ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var pedidas = comparticiones.Where(c => paquetes.ContainsKey(c.PaqueteId))
            .Select(c => (c, Numero: c.RevisionFijada ?? paquetes[c.PaqueteId].RevisionPublicada!.Value))
            .ToList();
        var numeros = pedidas.Select(x => x.Numero).Distinct().ToList();
        var revisiones = (await contexto.RevisionesPaquete.AsNoTracking()
                .Where(r => idsPaquetes.Contains(r.PaqueteId) && numeros.Contains(r.Numero))
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(r => (r.PaqueteId, r.Numero));

        var definiciones = pedidas
            .Where(x => revisiones.ContainsKey((x.c.PaqueteId, x.Numero)))
            .Select(x => (x.c, Revision: revisiones[(x.c.PaqueteId, x.Numero)], Definicion: FotoPaquete.LeerDefinicion(revisiones[(x.c.PaqueteId, x.Numero)].Definicion)))
            .ToList();
        var datos = await DatosAsync(definiciones.SelectMany(d => d.Definicion).ToList(), ct).ConfigureAwait(false);
        var aplicativos = (await lector.ListarAsync(ct).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var extras = await ExtrasDeInvitacionAsync(persona, organizacion, ahora, ct).ConfigureAwait(false);

        var vista = new List<PaqueteDelPortal>();
        foreach (var (comparticion, revision, definicion) in definiciones)
        {
            var paquete = paquetes[comparticion.PaqueteId];
            var contratos = new List<ContratoDelPortal>();
            foreach (var entrada in definicion)
            {
                // Un contrato que volvió a interno (o ya no existe) deja de verse en el portal (falla cerrada).
                if (datos.Contratos.GetValueOrDefault(entrada.ContratoId) is not { Visibilidad: VisibilidadContrato.Compartible } contrato
                    || datos.Versiones.GetValueOrDefault(entrada.VersionId) is not { } version || version.ContratoId != contrato.Id)
                {
                    continue;
                }
                var nivel = NivelEn(permisos, PermisosContratos.PortalVer, paquete.Id, contrato.AplicativoId, organizacion, aplicativos);
                if (nivel == NivelPortal.Ninguno)
                {
                    continue;
                }
                contratos.Add(new ContratoDelPortal(entrada, contrato, version, GuiasVisibles(entrada, datos), nivel));
            }
            if (contratos.Count == 0)
            {
                continue;
            }
            var nivelPaquete = contratos.Max(c => c.Nivel);
            vista.Add(new PaqueteDelPortal(comparticion, paquete, revision, nivelPaquete, contratos,
                PuedeExportar: comparticion.PuedeExportar && nivelPaquete == NivelPortal.Tecnico
                    && (Cubre(permisos, PermisosContratos.ExportacionesSolicitar, paquete.Id, contratos, organizacion, aplicativos) || extras.Contains(ExtraExportar)),
                PuedeSandbox: comparticion.PuedeSandbox && nivelPaquete == NivelPortal.Tecnico
                    && (Cubre(permisos, PermisosContratos.SandboxUsar, paquete.Id, contratos, organizacion, aplicativos) || extras.Contains(ExtraSandbox))));
        }
        return new VistaPortal(organizacion, persona,
            [.. vista.OrderBy(x => x.Paquete.Nombre, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Paquete.Id)]);
    }

    /// <summary>El mayor nivel que dan las asignaciones de la persona con <paramref name="permiso"/> sobre un contrato de un paquete.</summary>
    private NivelPortal NivelEn(
        PermisosEfectivos permisos, string permiso, Guid paquete, Guid aplicativoDelContrato, Guid organizacion, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        var nivel = NivelPortal.Ninguno;
        foreach (var concesion in permisos.Concesiones.Where(c => catalogo.PermisosDe(c.Rol).Contains(permiso)))
        {
            if (CoberturaDe(concesion.Alcance, paquete, aplicativoDelContrato, organizacion, aplicativos) is { } cobertura)
            {
                var deRol = AlcanceRolesPortal.Nivel(concesion.Rol, catalogo.Rol(concesion.Rol)?.Tipo, cobertura);
                nivel = deRol > nivel ? deRol : nivel;
            }
        }
        return nivel;
    }

    /// <summary>¿Alguna asignación con <paramref name="permiso"/> cubre algún contrato visible del paquete?</summary>
    private bool Cubre(
        PermisosEfectivos permisos, string permiso, Guid paquete, IReadOnlyList<ContratoDelPortal> contratos, Guid organizacion,
        IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos) =>
        contratos.Any(c => permisos.Concesiones.Any(concesion => catalogo.PermisosDe(concesion.Rol).Contains(permiso)
            && CoberturaDe(concesion.Alcance, paquete, c.Contrato.AplicativoId, organizacion, aplicativos) is not null));

    /// <summary>Cómo cubre un alcance de asignación a un contrato de un paquete; <c>null</c> si no lo cubre.</summary>
    internal static AlcanceRolesPortal.Cobertura? CoberturaDe(
        AlcanceAsignacion alcance, Guid paquete, Guid aplicativoDelContrato, Guid organizacion, IReadOnlyDictionary<Guid, AplicativoLeido> aplicativos)
    {
        if (alcance.EsGlobal || (alcance.Tipo == TiposAlcance.Organizacion && Guid.TryParse(alcance.Id, out var org) && org == organizacion))
        {
            return AlcanceRolesPortal.Cobertura.Organizacion;
        }
        if (alcance.Tipo == TiposAlcance.Paquete && Guid.TryParse(alcance.Id, out var p) && p == paquete)
        {
            return AlcanceRolesPortal.Cobertura.Paquete;
        }
        if (alcance.Tipo == TiposAlcance.Aplicativo && Guid.TryParse(alcance.Id, out var a)
            && ReglasGuias.ConAncestros(aplicativoDelContrato, aplicativos).Contains(a))
        {
            return AlcanceRolesPortal.Cobertura.Aplicativo;
        }
        return null;
    }

    /// <summary>Permisos extra (<c>exportar</c>, <c>sandbox</c>) de las invitaciones aceptadas y vigentes de la persona (§6.2).</summary>
    private async Task<HashSet<string>> ExtrasDeInvitacionAsync(Guid persona, Guid organizacion, DateTimeOffset ahora, CancellationToken ct)
    {
        var extras = await plataforma.Invitaciones.AsNoTracking()
            .Where(i => i.PersonaId == persona && i.OrganizacionId == organizacion && i.Estado == EstadoInvitacion.Aceptada
                && (i.AccesoVenceEn == null || i.AccesoVenceEn > ahora))
            .Select(i => i.PermisosExtra)
            .ToListAsync(ct).ConfigureAwait(false);
        var conjunto = new HashSet<string>(StringComparer.Ordinal);
        foreach (var json in extras)
        {
            try
            {
                conjunto.UnionWith(JsonSerializer.Deserialize<List<string>>(json) ?? []);
            }
            catch (JsonException)
            {
                // Un JSON dañado no concede nada.
            }
        }
        return conjunto;
    }

    /// <summary>Una guía se ve si sigue siendo del contrato, compartible y publicada, y su revisión fijada es compartible.</summary>
    private static List<GuiaDelPortal> GuiasVisibles(EntradaPaquete entrada, DatosPortal datos) =>
    [
        .. entrada.Guias
            .Select(g => (g, Guia: datos.Guias.GetValueOrDefault(g.GuiaId), Revision: datos.RevisionesGuia.GetValueOrDefault((g.GuiaId, g.Revision))))
            .Where(x => x.Guia is { Visibilidad: VisibilidadGuia.Compartible, RevisionPublicada: not null } guia && guia.ContratoId == entrada.ContratoId
                && x.Revision is { Visibilidad: VisibilidadGuia.Compartible })
            .Select(x => new GuiaDelPortal(x.g.GuiaId, x.g.Revision, x.Revision!.Titulo, x.Revision.Operacion)),
    ];

    private sealed record DatosPortal(
        IReadOnlyDictionary<Guid, Contrato> Contratos,
        IReadOnlyDictionary<Guid, VersionContratoSinContenido> Versiones,
        IReadOnlyDictionary<Guid, GuiaContrato> Guias,
        IReadOnlyDictionary<(Guid GuiaId, int Numero), RevisionGuia> RevisionesGuia);

    /// <summary>Contratos, versiones (sin el spec) y guías (sin el cuerpo) de las definiciones, en pocas consultas.</summary>
    private async Task<DatosPortal> DatosAsync(IReadOnlyList<EntradaPaquete> entradas, CancellationToken ct)
    {
        var idsContratos = entradas.Select(e => e.ContratoId).Distinct().ToList();
        var idsVersiones = entradas.Select(e => e.VersionId).Distinct().ToList();
        var idsGuias = entradas.SelectMany(e => e.Guias.Select(g => g.GuiaId)).Distinct().ToList();
        var numerosGuia = entradas.SelectMany(e => e.Guias.Select(g => g.Revision)).Distinct().ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => idsContratos.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        var versiones = await SinContenido(contexto.Versiones.AsNoTracking().Where(v => idsVersiones.Contains(v.Id))).ToDictionaryAsync(v => v.Id, ct).ConfigureAwait(false);
        var guias = await contexto.Guias.AsNoTracking().Where(g => idsGuias.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct).ConfigureAwait(false);
        var revisiones = (await contexto.RevisionesGuia.AsNoTracking()
                .Where(r => idsGuias.Contains(r.GuiaId) && numerosGuia.Contains(r.Numero))
                .Select(r => new RevisionGuia
                {
                    Id = r.Id, GuiaId = r.GuiaId, Numero = r.Numero, Origen = r.Origen, RestauradaDe = r.RestauradaDe, Titulo = r.Titulo,
                    Contenido = string.Empty, Operacion = r.Operacion, AplicaDesde = r.AplicaDesde, AplicaHasta = r.AplicaHasta, Visibilidad = r.Visibilidad,
                    Nota = string.Empty, AutorPersonaId = r.AutorPersonaId, GuardadaEn = r.GuardadaEn, Huella = r.Huella,
                })
                .ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(r => (r.GuiaId, r.Numero));
        return new DatosPortal(contratos, versiones, guias, revisiones);
    }
}
