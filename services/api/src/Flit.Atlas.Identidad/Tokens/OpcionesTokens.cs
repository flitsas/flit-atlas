using Flit.Atlas.ServiceDefaults;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>Servidor de tokens de OpenIddict (sección <c>Identidad:Tokens</c>, ATLAS-ADR-0024).</summary>
public sealed class OpcionesTokens
{
    public const string Seccion = "Identidad:Tokens";
    public const string ClaveCertificadosFirma = Seccion + ":Certificados:Firma";
    public const string ClaveCertificadosCifrado = Seccion + ":Certificados:Cifrado";
    public const string ClaveClaveCertificados = Seccion + ":Certificados:Clave";

    /// <summary>Audiencia de los tokens que acepta atlas-api.</summary>
    public const string AudienciaApi = "atlas-api";

    /// <summary>Tope del prompt maestro §6.3 para los tokens de acceso.</summary>
    public static readonly TimeSpan MaximoAcceso = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Emisor (<c>iss</c>) fijo, p. ej. <c>https://atlas.flitsas.com/</c>. Sin valor, OpenIddict lo deduce del host de la
    /// petición (ya corregido por <c>UseForwardedHeaders</c>).
    /// </summary>
    public Uri? Emisor { get; set; }

    /// <summary>Vida de los tokens de acceso: como máximo <see cref="MaximoAcceso"/>.</summary>
    public TimeSpan DuracionAcceso { get; set; } = MaximoAcceso;

    /// <summary>Vida de cada refresh token; cada canje entrega uno nuevo (rotación) con esta misma vida.</summary>
    public TimeSpan DuracionRefresh { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Tope de una concesión: pasado este tiempo desde la autorización, el refresh se rechaza y el cliente vuelve a
    /// pedir el ingreso, aunque haya rotado sus refresh tokens a tiempo.
    /// </summary>
    public TimeSpan DuracionMaximaConcesion { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// Margen en que un refresh ya canjeado se acepta otra vez (peticiones concurrentes del mismo cliente). Fuera de él,
    /// presentarlo revoca toda la cadena (detección de reutilización).
    /// </summary>
    public TimeSpan MargenReutilizacionRefresh { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Vida de los códigos de autorización.</summary>
    public TimeSpan DuracionCodigo { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Clientes registrados por configuración; la llave es el <c>client_id</c>.</summary>
    public Dictionary<string, ClienteTokens> Clientes { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Errores de la configuración, para fallar el arranque con todos a la vez.</summary>
    public IReadOnlyList<string> Errores(RegistroScopes scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        var errores = new List<string>();
        if (DuracionAcceso <= TimeSpan.Zero || DuracionAcceso > MaximoAcceso)
        {
            errores.Add($"{Variable("DuracionAcceso")} debe estar entre 1 segundo y {MaximoAcceso} (prompt maestro §6.3).");
        }
        if (DuracionRefresh <= TimeSpan.Zero || DuracionMaximaConcesion < DuracionRefresh)
        {
            errores.Add($"{Variable("DuracionRefresh")} debe ser positiva y no mayor que {Variable("DuracionMaximaConcesion")}.");
        }
        if (MargenReutilizacionRefresh < TimeSpan.Zero || MargenReutilizacionRefresh > TimeSpan.FromMinutes(1))
        {
            errores.Add($"{Variable("MargenReutilizacionRefresh")} debe estar entre 0 y 1 minuto.");
        }
        if (Emisor is { } emisor && (!emisor.IsAbsoluteUri || emisor.Scheme != Uri.UriSchemeHttps))
        {
            errores.Add($"{Variable("Emisor")} debe ser una URL https absoluta.");
        }
        foreach (var (id, cliente) in Clientes)
        {
            errores.AddRange(cliente.Errores(id, scopes));
        }
        return errores;
    }

    internal static string Variable(string clave) => ConfiguracionRequerida.ComoVariableDeEntorno($"{Seccion}:{clave}");
}

public enum TipoCliente
{
    /// <summary>No guarda secretos (CLI, MCP en el equipo del usuario): código con PKCE y redirect de loopback.</summary>
    Publico,

    /// <summary>Servicio interno con secreto (desde SealedSecret): <c>client_credentials</c>.</summary>
    Confidencial,
}

/// <summary>Un cliente de OpenIddict registrado por configuración (<c>Identidad:Tokens:Clientes:&lt;client_id&gt;</c>).</summary>
public sealed class ClienteTokens
{
    public const string FlujoCodigo = "authorization_code";
    public const string FlujoRefresh = "refresh_token";
    public const string FlujoServicio = "client_credentials";

    private static readonly string[] FlujosValidos = [FlujoCodigo, FlujoRefresh, FlujoServicio];

    /// <summary>Scopes estándar de OIDC que un cliente puede pedir además de los de Atlas.</summary>
    internal static readonly string[] ScopesOidc = ["openid", "profile", "email", "offline_access"];

    public string? Nombre { get; set; }

    public TipoCliente Tipo { get; set; } = TipoCliente.Publico;

    /// <summary>Secreto de un cliente confidencial. Nunca en el repo: user secrets en local y SealedSecret en qa y pdn.</summary>
    public string? Secreto { get; set; }

    public List<string> Flujos { get; init; } = [];

    /// <summary>
    /// Redirect URIs exactas. Las de loopback (<c>http://127.0.0.1/...</c>) se registran <b>sin puerto</b>: el cliente
    /// elige uno libre al ejecutarse (RFC 8252 §7.3; OpenIddict compara sin el puerto solo en loopback).
    /// </summary>
    public List<string> RedirectUris { get; init; } = [];

    /// <summary>Scopes que el cliente puede pedir: de Atlas (del registro de scopes) y estándar de OIDC.</summary>
    public List<string> Scopes { get; init; } = [];

    /// <summary>Audiencias (<c>aud</c>) de sus tokens de acceso; por defecto, solo atlas-api.</summary>
    public List<string> Audiencias { get; init; } = [];

    public IReadOnlyList<string> AudienciasEfectivas => Audiencias.Count > 0 ? Audiencias : [OpcionesTokens.AudienciaApi];

    internal IEnumerable<string> Errores(string id, RegistroScopes scopes)
    {
        var prefijo = $"Cliente «{id}»";
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            yield return $"{prefijo}: el client_id solo admite letras, dígitos, «-», «_» y «.» (máximo 100).";
        }
        if (Flujos.Count == 0 || Flujos.Any(f => !FlujosValidos.Contains(f)))
        {
            yield return $"{prefijo}: Flujos debe tener uno o más de {string.Join(", ", FlujosValidos)}.";
        }
        if (Tipo == TipoCliente.Publico)
        {
            if (!string.IsNullOrEmpty(Secreto))
            {
                yield return $"{prefijo}: un cliente público no lleva secreto.";
            }
            if (Flujos.Contains(FlujoServicio))
            {
                yield return $"{prefijo}: client_credentials solo para clientes confidenciales.";
            }
        }
        else if (string.IsNullOrWhiteSpace(Secreto))
        {
            yield return $"{prefijo}: falta {OpcionesTokens.Variable($"Clientes:{id}:Secreto")} (cliente confidencial; " +
                "en qa y pdn llega desde un SealedSecret).";
        }
        else if (Secreto.Length < 32)
        {
            yield return $"{prefijo}: el secreto debe tener al menos 32 caracteres aleatorios.";
        }
        if (Flujos.Contains(FlujoRefresh) && !Flujos.Contains(FlujoCodigo))
        {
            yield return $"{prefijo}: refresh_token solo tiene sentido con authorization_code.";
        }
        if (Flujos.Contains(FlujoCodigo) && RedirectUris.Count == 0)
        {
            yield return $"{prefijo}: authorization_code necesita al menos una redirect URI.";
        }
        foreach (var uri in RedirectUris)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u) || !string.IsNullOrEmpty(u.Fragment)
                || !(u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback)))
            {
                yield return $"{prefijo}: redirect URI inválida «{uri}» (https, o http solo en loopback; sin fragmento).";
            }
        }
        foreach (var scope in Scopes.Where(s => !ScopesOidc.Contains(s) && !scopes.Existe(s)))
        {
            yield return $"{prefijo}: el scope «{scope}» no lo declara ningún módulo.";
        }
        if (Scopes.Contains("offline_access") && !Flujos.Contains(FlujoRefresh))
        {
            yield return $"{prefijo}: offline_access necesita el flujo refresh_token.";
        }
        if (Audiencias.Any(string.IsNullOrWhiteSpace))
        {
            yield return $"{prefijo}: hay una audiencia vacía.";
        }
    }
}
