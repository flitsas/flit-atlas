using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Identidad.Entra;

/// <summary>Cargo, área y grupos de un empleado según Microsoft Graph.</summary>
public sealed record PerfilEmpleado(string? Cargo, string? Area, IReadOnlyCollection<Guid>? Grupos);

public interface IPerfilEmpleados
{
    /// <summary>Nunca falla: si Graph no responde, devuelve un perfil vacío y el ingreso sigue con los datos del token.</summary>
    Task<PerfilEmpleado> ObtenerAsync(Guid oid, CancellationToken ct);
}

/// <summary>
/// Lee el perfil con un token de aplicación (<c>User.Read.All</c> y <c>GroupMember.Read.All</c>, F0.4 ítem 5.4). Los
/// grupos se leen de Graph, sin depender del claim <c>groups</c> del token ni de su límite (overage).
/// </summary>
public sealed partial class PerfilGraph(
    HttpClient http,
    IAuthorizationHeaderProvider autorizacion,
    ILogger<PerfilGraph> log) : IPerfilEmpleados
{
    public const string ScopeGraph = "https://graph.microsoft.com/.default";

    public async Task<PerfilEmpleado> ObtenerAsync(Guid oid, CancellationToken ct)
    {
        try
        {
            var cabecera = await autorizacion.CreateAuthorizationHeaderForAppAsync(ScopeGraph, cancellationToken: ct);
            http.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(cabecera);

            var usuario = await http.GetFromJsonAsync<UsuarioGraph>(
                $"v1.0/users/{oid}?$select=jobTitle,department", ct);

            var grupos = new List<Guid>();
            string? siguiente = $"v1.0/users/{oid}/transitiveMemberOf/microsoft.graph.group?$select=id&$top=999";
            while (siguiente is not null)
            {
                var pagina = await http.GetFromJsonAsync<PaginaGrupos>(siguiente, ct);
                grupos.AddRange(pagina?.Valores.Select(g => g.Id) ?? []);
                siguiente = pagina?.Siguiente;
            }
            return new PerfilEmpleado(usuario?.Cargo, usuario?.Area, grupos);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Graph caído no debe impedir que un empleado entre.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            NoSeLeyoGraph(log, ex);
            return new PerfilEmpleado(null, null, null);
        }
    }

    private sealed record UsuarioGraph(
        [property: JsonPropertyName("jobTitle")] string? Cargo,
        [property: JsonPropertyName("department")] string? Area);

    private sealed record GrupoGraph([property: JsonPropertyName("id")] Guid Id);

    private sealed record PaginaGrupos(
        [property: JsonPropertyName("value")] List<GrupoGraph> Valores,
        [property: JsonPropertyName("@odata.nextLink")] string? Siguiente);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo leer el perfil del empleado en Graph; se usan los datos del token")]
    private static partial void NoSeLeyoGraph(ILogger log, Exception ex);
}

/// <summary>Sin Entra configurado (local con el proveedor simulado): no hay Graph.</summary>
public sealed class SinPerfilEmpleados : IPerfilEmpleados
{
    public Task<PerfilEmpleado> ObtenerAsync(Guid oid, CancellationToken ct) =>
        Task.FromResult(new PerfilEmpleado(null, null, null));
}
