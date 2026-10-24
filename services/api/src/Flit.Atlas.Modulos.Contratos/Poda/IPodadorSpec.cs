using Flit.Atlas.Modulos.Contratos.Datos;

namespace Flit.Atlas.Modulos.Contratos.Poda;

/// <summary>
/// Poda de un spec a lo que se comparte con un cliente (HU 4.2, plan 00 §6.2 regla 3 y §8.6, ATLAS-ADR-0038): solo las
/// operaciones seleccionadas, los componentes que alcanzan, sin nada marcado como interno y con los servidores públicos
/// del ambiente destino. Es <b>fail-closed</b>: ante cualquier duda lanza <see cref="PodaException"/>; jamás devuelve el spec
/// completo ni uno a medio podar.
/// </summary>
public interface IPodadorSpec
{
    TipoContrato Tipo { get; }

    /// <summary>Las operaciones (o canales) que se pueden seleccionar, con la misma clave de <c>contratos_operacion</c>.</summary>
    /// <exception cref="PodaException">El spec no se puede leer o no es de este tipo.</exception>
    IReadOnlyList<OperacionPodable> Operaciones(string contenido);

    /// <exception cref="PodaException">Por cualquier motivo de <see cref="MotivoPoda"/>; nunca hay resultado parcial.</exception>
    SpecPodado Podar(SolicitudPoda solicitud);
}

/// <summary>Una operación del spec que se puede seleccionar.</summary>
/// <param name="Clave">La de <c>contratos_operacion</c> (<c>GET /tramites/{id}</c>, <c>send tramites.radicado</c>).</param>
/// <param name="Interna">Marcada con <c>x-internal: true</c> (ella o su ruta o canal): no se puede compartir.</param>
public sealed record OperacionPodable(string Clave, bool Interna);

/// <summary>Ambientes a los que se poda (los que ve un cliente).</summary>
public static class AmbientesPoda
{
    public const string Sandbox = "sandbox";
    public const string Qa = "qa";
    public const string Pdn = "pdn";

    public static IReadOnlySet<string> Todos { get; } = new HashSet<string>(StringComparer.Ordinal) { Sandbox, Qa, Pdn };
}

/// <summary>Ambiente destino y sus URL públicas. Sin URL, el spec podado sale sin <c>servers</c> (nunca con los internos).</summary>
public sealed record DestinoPoda(string Ambiente, IReadOnlyList<Uri> Urls);

/// <summary>Qué podar.</summary>
/// <param name="Contenido">El spec completo, tal como se publicó (JSON o YAML).</param>
/// <param name="Claves">Claves de <c>contratos_operacion</c> seleccionadas; una que no exista en el spec es error.</param>
public sealed record SolicitudPoda(string Contenido, IReadOnlyCollection<string> Claves, DestinoPoda Destino);

/// <summary>Lo que quedó y lo que se quitó.</summary>
/// <param name="Operaciones">Claves conservadas, en el orden del spec.</param>
/// <param name="ComponentesConservados"><c>schemas/Tramite</c>, <c>securitySchemes/bearer</c>, <c>channels/tramites</c>…</param>
/// <param name="NodosInternosEliminados">Nodos quitados por <c>x-internal: true</c> o <c>x-interno: true</c>.</param>
/// <param name="Servidores">URL de los servidores del resultado.</param>
public sealed record ResumenPoda(
    IReadOnlyList<string> Operaciones,
    IReadOnlyList<string> ComponentesConservados,
    IReadOnlyList<string> ComponentesEliminados,
    IReadOnlyList<string> EtiquetasEliminadas,
    int NodosInternosEliminados,
    IReadOnlyList<string> Servidores,
    IReadOnlyList<string> Advertencias);

/// <summary>El spec podado, en el mismo formato en que llegó.</summary>
public sealed record SpecPodado(string Contenido, FormatoSpec Formato, ResumenPoda Resumen);

/// <summary>Por qué no se pudo podar. El texto de <see cref="PodaException.Codigo"/> es estable.</summary>
public enum MotivoPoda
{
    /// <summary><c>spec_invalida</c>: no se puede leer o no es OpenAPI 3.0/3.1 o AsyncAPI 2.x/3.x.</summary>
    SpecInvalida,

    /// <summary><c>seleccion_vacia</c>.</summary>
    SeleccionVacia,

    /// <summary><c>claves_desconocidas</c>: alguna clave seleccionada no está en el spec.</summary>
    ClavesDesconocidas,

    /// <summary><c>operacion_interna</c>: se seleccionó algo marcado <c>x-internal</c>.</summary>
    OperacionInterna,

    /// <summary><c>referencia_externa</c>: una <c>$ref</c> alcanzable no es local (<c>#/...</c>).</summary>
    ReferenciaExterna,

    /// <summary><c>referencia_rota</c>: una <c>$ref</c> alcanzable no resuelve (o apunta a algo interno o podado).</summary>
    ReferenciaRota,

    /// <summary><c>destino_invalido</c>: ambiente desconocido o URL que no es pública.</summary>
    DestinoInvalido,

    /// <summary><c>verificacion_fallida</c>: la revisión final del resultado encontró algo que no debía quedar.</summary>
    VerificacionFallida,
}

/// <summary>La poda no se pudo hacer; no hay resultado.</summary>
public sealed class PodaException(MotivoPoda motivo, string mensaje, IReadOnlyList<string>? detalles = null) : Exception(mensaje)
{
    public MotivoPoda Motivo { get; } = motivo;

    /// <summary>Claves, referencias o URL que causaron el error (las primeras <see cref="MaximoDetalles"/>).</summary>
    public IReadOnlyList<string> Detalles { get; } = [.. (detalles ?? []).Take(MaximoDetalles)];

    public const int MaximoDetalles = 50;

    public string Codigo => Motivo switch
    {
        MotivoPoda.SpecInvalida => "spec_invalida",
        MotivoPoda.SeleccionVacia => "seleccion_vacia",
        MotivoPoda.ClavesDesconocidas => "claves_desconocidas",
        MotivoPoda.OperacionInterna => "operacion_interna",
        MotivoPoda.ReferenciaExterna => "referencia_externa",
        MotivoPoda.ReferenciaRota => "referencia_rota",
        MotivoPoda.DestinoInvalido => "destino_invalido",
        _ => "verificacion_fallida",
    };
}

/// <summary>El podador de cada tipo. proto no se poda (no entra a paquetes de clientes en el MVP).</summary>
public static class Podadores
{
    public static IPodadorSpec OpenApi { get; } = new PodadorOpenApi();

    public static IPodadorSpec AsyncApi { get; } = new PodadorAsyncApi();

    public static IPodadorSpec? Para(TipoContrato tipo) => tipo switch
    {
        TipoContrato.Openapi => OpenApi,
        TipoContrato.Asyncapi => AsyncApi,
        _ => null,
    };
}
