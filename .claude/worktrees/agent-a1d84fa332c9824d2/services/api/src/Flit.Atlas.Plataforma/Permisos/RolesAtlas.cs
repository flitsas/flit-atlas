using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Un rol del §7 tal como lo define el código; el migrador lo aplica a <c>plataforma_rol</c>.</summary>
public sealed record DefinicionRol(string Codigo, string Nombre, TipoRol Tipo, string Descripcion);

/// <summary>
/// Roles internos (§7.2), externos (§7.3) e implícitos de Atlas (ATLAS-ADR-0023). Los roles no los crea un módulo:
/// los módulos solo dicen, en su manifiesto, qué roles reciben cada uno de sus permisos.
/// </summary>
public static class RolesAtlas
{
    public const string Superadmin = "atlas.superadmin";
    public const string LiderTecnico = "atlas.lider_tecnico";
    public const string Desarrollador = "atlas.desarrollador";
    public const string Soporte = "atlas.soporte";
    public const string Negocio = "atlas.negocio";
    public const string GobiernoIa = "atlas.gobierno_ia";
    public const string Auditor = "atlas.auditor";

    /// <summary>
    /// Todo empleado activo lo tiene sin asignarlo (§7.2: «cualquier empleado sin rol específico»); los demás roles
    /// internos se suman a él.
    /// </summary>
    public const string Lector = "atlas.lector";

    public const string Integrador = "ext.integrador";
    public const string ClienteFuncional = "ext.cliente_funcional";
    public const string AsesorComercial = "ext.asesor_comercial";
    public const string Proveedor = "ext.proveedor";
    public const string AdminOrganizacion = "ext.admin_organizacion";

    /// <summary>
    /// Implícito: responsable principal o secundario de un aplicativo (§7.2), con alcance a ese aplicativo. Lo entrega
    /// <see cref="IProveedorRolesImplicitos"/> (F2); no se asigna a mano.
    /// </summary>
    public const string Responsable = "atlas.responsable";

    public static IReadOnlyList<DefinicionRol> Todos { get; } =
    [
        new(Superadmin, "Superadministrador", TipoRol.Interno, "Administración de Atlas: todo, incluida la configuración, los roles, los módulos y la auditoría completa."),
        new(LiderTecnico, "Líder técnico", TipoRol.Interno, "Gestiona catálogo, responsables y rotación (aprueba cambios y compensaciones), paquetes, comparticiones e invitaciones; publica manualmente por excepción."),
        new(Desarrollador, "Desarrollador", TipoRol.Interno, "Ve todo lo interno_ti, propone cambios a documentos, participa en la rotación y solicita cambios de turno."),
        new(Soporte, "Soporte", TipoRol.Interno, "Mesa de ayuda e implementación: interno_negocio y notas de soporte; contratos internos y rotación en lectura."),
        new(Negocio, "Negocio", TipoRol.Interno, "Comercial interno, gerencia y capacitación: interno_negocio y fichas comerciales, sin detalle técnico ni infraestructura."),
        new(GobiernoIa, "Gobierno de IA", TipoRol.Interno, "Comité y responsable de IA: módulo de gobierno de IA completo."),
        new(Auditor, "Auditor", TipoRol.Interno, "Seguridad y SGSI: lectura de auditoría y evidencias, sin edición."),
        new(Lector, "Lector", TipoRol.Interno, "Todo empleado: catálogo, responsables, calendario de soporte y documentación interno_negocio en lectura."),
        new(Integrador, "Integrador", TipoRol.Externo, "Equipo técnico de un cliente que consume las APIs: paquetes de contratos compartidos, guías, changelog y avisos de deprecación."),
        new(ClienteFuncional, "Cliente funcional", TipoRol.Externo, "Organismos de tránsito, CEA y gestoras: manuales y documentación funcional de los productos contratados."),
        new(AsesorComercial, "Asesor comercial", TipoRol.Externo, "Asesores comerciales externos: fichas comerciales y funcionales de los productos asignados."),
        new(Proveedor, "Proveedor", TipoRol.Externo, "Consultores y proveedores: solo lo concedido explícitamente, con vencimiento obligatorio."),
        new(AdminOrganizacion, "Administrador de la organización", TipoRol.Externo, "Responsable del lado del cliente: invita y retira colegas de su propia organización dentro del alcance ya otorgado."),
        new(Responsable, "Responsable del aplicativo", TipoRol.Implicito, "Implícito: responsable principal o secundario; edita documentos, guías y diagramas y arma paquetes solo de su aplicativo."),
    ];

    /// <summary>Internos salvo el superadministrador (que recibe todo sin declararlo).</summary>
    public static readonly string[] Empleados = [LiderTecnico, Desarrollador, Soporte, Negocio, GobiernoIa, Auditor, Lector];

    public static readonly string[] Externos = [Integrador, ClienteFuncional, AsesorComercial, Proveedor, AdminOrganizacion];
}
