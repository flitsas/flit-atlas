using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Flit.Atlas.Plataforma.Render;

/// <summary>
/// <see cref="IRenderDiff"/> en memoria para las pruebas de los módulos (no levanta atlas-render). Imita las reglas del
/// servicio: AsyncAPI y proto dan <see cref="MotivoErrorDiff.NoSoportado"/>, una spec vacía da
/// <see cref="MotivoErrorDiff.SpecInvalida"/>, specs iguales no tienen cambios y specs distintas dan un cambio
/// informativo, salvo que la prueba programe la respuesta con <see cref="Programar"/> o <see cref="Responder"/>. Guarda
/// las solicitudes recibidas.
/// </summary>
public sealed class RenderDiffSimulado : IRenderDiff
{
    private readonly ConcurrentDictionary<(string, string), Func<SolicitudDiff, ResultadoDiff>> _programadas = new();
    private readonly ConcurrentQueue<SolicitudDiff> _recibidas = new();

    /// <summary>Si se define, responde todo lo que no esté programado por par de hashes.</summary>
    public Func<SolicitudDiff, ResultadoDiff>? Responder { get; set; }

    /// <summary>Solicitudes recibidas, en orden.</summary>
    public IReadOnlyCollection<SolicitudDiff> Recibidas => _recibidas;

    /// <summary>Programa la respuesta para un par de specs (por su contenido).</summary>
    public void Programar(string baseSpec, string revision, Func<SolicitudDiff, ResultadoDiff> respuesta) =>
        _programadas[(Hash(baseSpec), Hash(revision))] = respuesta;

    public Task<ResultadoDiff> CalcularAsync(SolicitudDiff solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        ct.ThrowIfCancellationRequested();
        _recibidas.Enqueue(solicitud);

        if (!TiposContrato.ConDiff.Contains(solicitud.Tipo))
        {
            var conocido = solicitud.Tipo is TiposContrato.AsyncApi or TiposContrato.Proto;
            throw new RenderDiffException(conocido ? "Diff no soportado para este tipo" : "Tipo o idioma no válidos",
                conocido ? MotivoErrorDiff.NoSoportado : MotivoErrorDiff.Rechazado, conocido ? 422 : 400);
        }
        if (string.IsNullOrWhiteSpace(solicitud.Base) || string.IsNullOrWhiteSpace(solicitud.Revision))
        {
            var lado = string.IsNullOrWhiteSpace(solicitud.Base) ? "base" : "revision";
            throw new RenderDiffException($"La spec {lado} no es un OpenAPI válido", MotivoErrorDiff.SpecInvalida, 422, lado);
        }

        var hashBase = Hash(solicitud.Base);
        var hashRevision = Hash(solicitud.Revision);
        if (_programadas.TryGetValue((hashBase, hashRevision), out var programada))
        {
            return Task.FromResult(programada(solicitud));
        }
        if (Responder is { } responder)
        {
            return Task.FromResult(responder(solicitud));
        }

        var iguales = hashBase == hashRevision;
        IReadOnlyList<CambioContrato> cambios = iguales
            ? []
            : [new CambioContrato { Regla = "simulado", Nivel = NivelesCambio.Informativo, Texto = "cambio simulado" }];
        return Task.FromResult(new ResultadoDiff
        {
            Tipo = TiposContrato.OpenApi,
            Motor = "simulado",
            Idioma = solicitud.Idioma,
            HashBase = hashBase,
            HashRevision = hashRevision,
            SinDiferencias = iguales,
            Resumen = new ResumenDiff(cambios.Count, 0, 0, cambios.Count, 0, false),
            Cambios = cambios,
            Breaking = [],
            Changelog = iguales ? "Sin cambios.\n" : "- cambio simulado\n",
        });
    }

    /// <summary>El mismo hash que usa atlas-render: SHA-256 de los bytes UTF-8, en hexadecimal minúscula.</summary>
    public static string Hash(string spec) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(spec)));
}
