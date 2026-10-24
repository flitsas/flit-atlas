using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Flit.Atlas.Identidad.Entra;
using Microsoft.Identity.Abstractions;

namespace Flit.Atlas.Identidad.Personas;

/// <summary>Un usuario de Entra ID como lo ve la sincronización. <see cref="Telefono"/> y el correo son datos personales.</summary>
/// <param name="Correo"><c>mail</c> o, si no tiene buzón, el <c>userPrincipalName</c>, en minúsculas (como en el ingreso).</param>
/// <param name="Habilitada"><c>accountEnabled</c> de Entra.</param>
/// <param name="EsMiembro"><c>userType = Member</c>; los invitados B2B (<c>Guest</c>) nunca se sincronizan.</param>
/// <param name="ConBuzon">Tiene <c>mail</c>: las cuentas sin buzón (de servicio, técnicas) no se crean como personas.</param>
public sealed record UsuarioEntra(
    Guid Id, string Nombre, string? Correo, string? Cargo, string? Area, string? Telefono, bool Habilitada, bool EsMiembro,
    bool ConBuzon = true);

/// <summary>Foto de perfil tal como la entrega Graph.</summary>
public sealed record FotoEntra(byte[] Contenido, string TipoContenido);

/// <summary>Lectura del directorio de Entra ID para la sincronización de personas (ATLAS-ADR-0030).</summary>
public interface IDirectorioEntra
{
    /// <summary><c>false</c> sin Entra configurado (local con el proveedor simulado): no hay a quién preguntar.</summary>
    bool Disponible { get; }

    /// <summary>Usuarios del grupo o del tenant, según el origen. Con <see cref="OrigenPersonas.Enlazadas"/> no lista nada.</summary>
    IAsyncEnumerable<UsuarioEntra> ListarAsync(OrigenPersonas origen, Guid? grupo, CancellationToken ct);

    /// <summary>Un usuario por su <c>oid</c>; <c>null</c> si ya no existe en Entra (404). Cualquier otra falla lanza.</summary>
    Task<UsuarioEntra?> ObtenerAsync(Guid oid, CancellationToken ct);

    /// <summary>Foto de perfil; <c>null</c> si no tiene, si no es una imagen o si pasa de <paramref name="tamanoMaximo"/>.</summary>
    Task<FotoEntra?> ObtenerFotoAsync(Guid oid, int tamanoMaximo, CancellationToken ct);
}

/// <summary>
/// Graph con el token de aplicación de Atlas: <c>User.Read.All</c> (perfil, <c>accountEnabled</c> y foto) y
/// <c>GroupMember.Read.All</c> (miembros del grupo), los mismos que ya usa <see cref="PerfilGraph"/>. No pide permisos
/// nuevos. Un 429 o un 5xx los reintenta el manejador de resiliencia de <c>ServiceDefaults</c> (respeta
/// <c>Retry-After</c>); si aun así falla, lanza y la corrida se reintenta entera más tarde.
/// </summary>
public sealed class DirectorioGraph(HttpClient http, IAuthorizationHeaderProvider autorizacion) : IDirectorioEntra
{
    internal const string Campos =
        "id,displayName,mail,userPrincipalName,jobTitle,department,mobilePhone,businessPhones,accountEnabled,userType";

    /// <summary>Tamaño de la foto que se pide: alcanza para los avatares (hasta 120 px) y pesa pocos KB.</summary>
    internal const string TamanoFoto = "120x120";

    private static readonly HashSet<string> TiposImagen = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
    };

    public bool Disponible => true;

    public async IAsyncEnumerable<UsuarioEntra> ListarAsync(
        OrigenPersonas origen, Guid? grupo, [EnumeratorCancellation] CancellationToken ct)
    {
        string? siguiente = origen switch
        {
            OrigenPersonas.Grupo => $"v1.0/groups/{grupo}/transitiveMembers/microsoft.graph.user?$select={Campos}&$top=999",
            OrigenPersonas.Tenant => $"v1.0/users?$filter=userType eq 'Member'&$select={Campos}&$top=999",
            _ => null,
        };
        while (siguiente is not null)
        {
            using var respuesta = await EnviarAsync(siguiente, ct);
            respuesta.EnsureSuccessStatusCode();
            var pagina = await respuesta.Content.ReadFromJsonAsync<PaginaUsuarios>(ct);
            foreach (var usuario in pagina?.Valores ?? [])
            {
                yield return usuario.ComoUsuario();
            }
            siguiente = pagina?.Siguiente;
        }
    }

    public async Task<UsuarioEntra?> ObtenerAsync(Guid oid, CancellationToken ct)
    {
        using var respuesta = await EnviarAsync($"v1.0/users/{oid}?$select={Campos}", ct);
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        respuesta.EnsureSuccessStatusCode();
        return (await respuesta.Content.ReadFromJsonAsync<UsuarioGraph>(ct))?.ComoUsuario();
    }

    public async Task<FotoEntra?> ObtenerFotoAsync(Guid oid, int tamanoMaximo, CancellationToken ct)
    {
        // Si la foto no tiene ese tamaño, Graph responde 404: se pide la original, que también respeta el tope.
        foreach (var ruta in new[] { $"v1.0/users/{oid}/photos/{TamanoFoto}/$value", $"v1.0/users/{oid}/photo/$value" })
        {
            using var respuesta = await EnviarAsync(ruta, ct, HttpCompletionOption.ResponseHeadersRead);
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }
            respuesta.EnsureSuccessStatusCode();
            var tipo = respuesta.Content.Headers.ContentType?.MediaType;
            if (tipo is null || !TiposImagen.Contains(tipo) || respuesta.Content.Headers.ContentLength > tamanoMaximo)
            {
                return null;
            }
            var contenido = await LeerConTopeAsync(await respuesta.Content.ReadAsStreamAsync(ct), tamanoMaximo, ct);
            return contenido is null ? null : new FotoEntra(contenido, tipo.ToLowerInvariant());
        }
        return null;
    }

    private async Task<HttpResponseMessage> EnviarAsync(
        string ruta, CancellationToken ct, HttpCompletionOption completar = HttpCompletionOption.ResponseContentRead)
    {
        var cabecera = await autorizacion.CabeceraAplicacionAsync(PerfilGraph.ScopeGraph, ct);
        using var peticion = new HttpRequestMessage(HttpMethod.Get, ruta);
        peticion.Headers.Authorization = AuthenticationHeaderValue.Parse(cabecera);
        // Los filtros y los cast de OData sobre el directorio pueden exigir la consulta avanzada.
        peticion.Headers.Add("ConsistencyLevel", "eventual");
        return await http.SendAsync(peticion, completar, ct);
    }

    /// <summary>Lee hasta el tope, sin confiar en <c>Content-Length</c>; <c>null</c> si lo pasa.</summary>
    private static async Task<byte[]?> LeerConTopeAsync(Stream origen, int tope, CancellationToken ct)
    {
        await using var _ = origen;
        using var destino = new MemoryStream();
        var bufer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await origen.ReadAsync(bufer, ct)) > 0)
        {
            if (destino.Length + leidos > tope)
            {
                return null;
            }
            destino.Write(bufer, 0, leidos);
        }
        return destino.Length == 0 ? null : destino.ToArray();
    }

    private sealed record UsuarioGraph(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("displayName")] string? Nombre,
        [property: JsonPropertyName("mail")] string? Correo,
        [property: JsonPropertyName("userPrincipalName")] string? Upn,
        [property: JsonPropertyName("jobTitle")] string? Cargo,
        [property: JsonPropertyName("department")] string? Area,
        [property: JsonPropertyName("mobilePhone")] string? Movil,
        [property: JsonPropertyName("businessPhones")] List<string>? TelefonosTrabajo,
        [property: JsonPropertyName("accountEnabled")] bool? Habilitada,
        [property: JsonPropertyName("userType")] string? Tipo)
    {
        public UsuarioEntra ComoUsuario()
        {
            var correo = string.IsNullOrWhiteSpace(Correo) ? Upn : Correo;
            return new UsuarioEntra(
                Id,
                string.IsNullOrWhiteSpace(Nombre) ? correo ?? Id.ToString() : Nombre.Trim(),
                string.IsNullOrWhiteSpace(correo) ? null : correo.Trim().ToLowerInvariant(),
                Limpio(Cargo),
                Limpio(Area),
                Limpio(Movil) ?? TelefonosTrabajo?.Select(Limpio).FirstOrDefault(t => t is not null),
                // Sin el campo (no debería pasar con $select) se asume habilitada: nunca se deshabilita por un dato que falta.
                Habilitada ?? true,
                // Graph puede omitir userType en cuentas viejas: solo «Guest» excluye.
                !string.Equals(Tipo, "Guest", StringComparison.OrdinalIgnoreCase),
                !string.IsNullOrWhiteSpace(Correo));
        }

        private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private sealed record PaginaUsuarios(
        [property: JsonPropertyName("value")] List<UsuarioGraph> Valores,
        [property: JsonPropertyName("@odata.nextLink")] string? Siguiente);
}

/// <summary>Sin Entra configurado (local con el proveedor simulado): no hay directorio que leer.</summary>
public sealed class SinDirectorioEntra : IDirectorioEntra
{
    public bool Disponible => false;

    public async IAsyncEnumerable<UsuarioEntra> ListarAsync(
        OrigenPersonas origen, Guid? grupo, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<UsuarioEntra?> ObtenerAsync(Guid oid, CancellationToken ct) => Task.FromResult<UsuarioEntra?>(null);

    public Task<FotoEntra?> ObtenerFotoAsync(Guid oid, int tamanoMaximo, CancellationToken ct) => Task.FromResult<FotoEntra?>(null);
}
