using System.Collections.Concurrent;
using System.Net;
using Flit.Atlas.Modulos.Contratos.Sandbox;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Microcks en memoria para las pruebas de aislamiento del portal (HU 4.7): guarda lo importado y responde 404 a todo mock.
/// Las pruebas con Microcks real (<see cref="SandboxMicrocksTests"/>) prueban los mocks.
/// </summary>
internal sealed class MicrocksEnMemoria : IClienteMicrocks
{
    public ConcurrentDictionary<string, string> Servicios { get; } = new(StringComparer.Ordinal);

    public bool Disponible => true;

    public Task ImportarAsync(string servicio, string version, string especificacionJson, CancellationToken ct)
    {
        Servicios[$"{servicio}:{version}"] = especificacionJson;
        return Task.CompletedTask;
    }

    public Task<bool> ExisteAsync(string servicio, string version, CancellationToken ct) => Task.FromResult(Servicios.ContainsKey($"{servicio}:{version}"));

    public Task BorrarAsync(string servicio, string version, CancellationToken ct)
    {
        Servicios.TryRemove($"{servicio}:{version}", out _);
        return Task.CompletedTask;
    }

    public Task<HttpResponseMessage> LlamarMockAsync(
        string servicio, string version, string ruta, string? consulta, HttpMethod metodo, IEnumerable<KeyValuePair<string, string[]>> cabeceras,
        byte[]? cuerpo, string? tipoContenido, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
}
