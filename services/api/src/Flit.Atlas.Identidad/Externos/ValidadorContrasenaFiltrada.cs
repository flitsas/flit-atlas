using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Revisa la contraseña de una cuenta local contra contraseñas filtradas (§6.2, ASVS V2.1.7, ATLAS-ADR-0025), como un
/// validador más de Identity (el largo mínimo de 12 lo exige <c>IdentityOptions.Password</c>):
/// <list type="number">
/// <item><b>Have I Been Pwned por rango</b> (k-anonimato): se envían solo los 5 primeros caracteres del SHA-1 de la
/// contraseña y se compara el resto aquí; la contraseña nunca sale del servidor. Con <c>Add-Padding</c>, el tamaño de la
/// respuesta no delata el rango.</item>
/// <item><b>Listas locales</b> (recursos del ensamblado): las 10.000 contraseñas filtradas más frecuentes de 12 o más
/// caracteres (<c>contrasenas-filtradas.txt</c>, de SecLists, licencia MIT; ASVS 5.0 V6.2.4 pide al menos 3000) y las
/// comunes en español y patrones de teclado (<c>contrasenas-comunes.txt</c>), más reglas obvias (el correo o el nombre
/// de la organización dentro, pocos caracteres distintos). Se revisan siempre, antes de HIBP.</item>
/// </list>
/// Si HIBP no responde (3 s), la revisión <b>falla abierto contra las listas locales</b>: la contraseña ya pasó las
/// 10.000 más frecuentes y las reglas, y el registro, la recuperación o el cambio de contraseña no se bloquean por la
/// caída de un tercero; queda una advertencia en el log (ATLAS-ADR-0049 §10).
/// </summary>
public sealed partial class ValidadorContrasenaFiltrada(
    IHttpClientFactory clientes,
    IOptions<OpcionesExternos> opciones,
    ILogger<ValidadorContrasenaFiltrada> log) : IPasswordValidator<UsuarioAtlas>
{
    public const string NombreCliente = "hibp";
    public const string CodigoFiltrada = "contrasena_filtrada";
    public const string CodigoComun = "contrasena_comun";

    private static readonly TimeSpan Tiempo = TimeSpan.FromSeconds(3);

    /// <summary>Recursos con las listas locales; cada línea es una contraseña en minúsculas (# para comentarios).</summary>
    internal static readonly string[] Listas =
    [
        "Flit.Atlas.Identidad.Externos.contrasenas-comunes.txt",
        "Flit.Atlas.Identidad.Externos.contrasenas-filtradas.txt",
    ];

    private static readonly Lazy<HashSet<string>> Comunes = new(CargarComunes);

    /// <summary>Cuántas contraseñas tiene la lista local (sin repetir); ASVS 5.0 V6.2.4 pide al menos 3000.</summary>
    internal static int TamanoListaLocal => Comunes.Value.Count;

    public async Task<IdentityResult> ValidateAsync(UserManager<UsuarioAtlas> manager, UsuarioAtlas user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);
        var contrasena = password ?? string.Empty;
        if (EsComun(contrasena, user.Email))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = CodigoComun,
                Description = "La contraseña es demasiado común o contiene tu correo. Usa una frase larga y propia.",
            });
        }
        if (opciones.Value.ConsultarContrasenasFiltradas && await EstaFiltradaAsync(contrasena).ConfigureAwait(false))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = CodigoFiltrada,
                Description = "Esa contraseña apareció en filtraciones públicas de datos. Elige otra.",
            });
        }
        return IdentityResult.Success;
    }

    /// <summary>Lista local y reglas; sin red.</summary>
    internal static bool EsComun(string contrasena, string? correo)
    {
        var normalizada = contrasena.Trim().ToLowerInvariant();
        if (normalizada.Length == 0 || normalizada.Distinct().Count() < 4 || Comunes.Value.Contains(normalizada))
        {
            return true;
        }
        var usuario = correo?.Split('@')[0].ToLowerInvariant();
        return (usuario is { Length: >= 4 } && normalizada.Contains(usuario, StringComparison.Ordinal))
            || normalizada.Contains("flitatlas", StringComparison.Ordinal);
    }

    /// <summary>
    /// ¿El SHA-1 aparece en el rango que devuelve HIBP? Si el servicio falla, <c>false</c>: falla abierto, porque la
    /// contraseña ya pasó las listas locales (ATLAS-ADR-0049 §10).
    /// </summary>
    internal async Task<bool> EstaFiltradaAsync(string contrasena)
    {
        // SHA-1 es lo que define el protocolo de rangos de HIBP; no protege nada aquí (CA5350 no aplica).
#pragma warning disable CA5350
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(contrasena)));
#pragma warning restore CA5350
        var prefijo = hash[..5];
        var sufijo = hash[5..];
        try
        {
            using var limite = new CancellationTokenSource(Tiempo);
            using var peticion = new HttpRequestMessage(HttpMethod.Get, new Uri($"range/{prefijo}", UriKind.Relative));
            peticion.Headers.Add("Add-Padding", "true");
            using var respuesta = await clientes.CreateClient(NombreCliente).SendAsync(peticion, limite.Token).ConfigureAwait(false);
            respuesta.EnsureSuccessStatusCode();
            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token).ConfigureAwait(false);
            foreach (var linea in cuerpo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var partes = linea.Split(':');
                if (partes.Length == 2 && string.Equals(partes[0], sufijo, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var veces) && veces > 0)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            NoRespondio(log, ex.GetType().Name);
            return false;
        }
    }

    private static HashSet<string> CargarComunes()
    {
        var lista = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recurso in Listas)
        {
            using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso)
                ?? throw new InvalidOperationException($"Falta el recurso {recurso}.");
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            while (lector.ReadLine() is { } linea)
            {
                var valor = linea.Trim();
                if (valor.Length > 0 && !valor.StartsWith('#'))
                {
                    lista.Add(valor.ToLowerInvariant());
                }
            }
        }
        return lista;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Have I Been Pwned no respondió ({Error}); la contraseña se revisó solo contra las listas locales")]
    private static partial void NoRespondio(ILogger log, string error);
}
