using System.ComponentModel;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Endpoints del servidor de tokens que atiende Atlas (ATLAS-ADR-0024). OpenIddict valida el protocolo (cliente,
/// redirect URI, PKCE, scopes, códigos y refresh tokens) y deja pasar la petición; aquí se decide <b>a quién</b> se
/// emite y con qué claims. Van bajo <c>/connect/*</c>, fuera de <c>/api</c> y del contrato OpenAPI: el contrato del
/// protocolo es el documento de descubrimiento.
/// </summary>
public static class EndpointsTokens
{
    /// <summary>Instante de la autorización original (privado: solo viaja en el código y el refresh, cifrados).</summary>
    internal const string ClaimConcedidoEn = "atlas.concedido_en";

    [Description("Quién es el portador del token de acceso: un usuario de Atlas (código con PKCE) o un servicio (client_credentials).")]
    public sealed record PortadorToken(
        [property: Description("sub del token: el id del usuario de Atlas o, para un servicio, su client_id.")] string Sub,
        [property: Description("Cliente que obtuvo el token.")] string? Cliente,
        [property: Description("empleado, externo o servicio.")] string TipoPrincipal,
        Guid? PersonaId,
        Guid? OrganizacionId,
        IReadOnlyList<string> Scopes,
        IReadOnlyList<string> Audiencias,
        DateTimeOffset? ExpiraEn);

    internal static IEndpointRouteBuilder MapServidorTokens(this IEndpointRouteBuilder rutas, RouteGroupBuilder identidad)
    {
        // OpenIddict autentica al cliente y al titular del código o del refresh antes del endpoint; el titular puede ser de
        // cualquier organización: alcance del sistema sin sesión (ATLAS-ADR-0050).
        rutas.MapMethods(ServidorTokens.RutaAutorizar, [HttpMethods.Get, HttpMethods.Post], AutorizarAsync).ExcludeFromDescription().ConAlcanceDelSistemaSinSesion();
        rutas.MapGet(ServidorTokens.RutaContinuar, Continuar).ExcludeFromDescription();
        rutas.MapPost(ServidorTokens.RutaToken, EmitirAsync).ExcludeFromDescription().ConAlcanceDelSistemaSinSesion();
        rutas.MapMethods(ServidorTokens.RutaUserinfo, [HttpMethods.Get, HttpMethods.Post], UserinfoAsync).ExcludeFromDescription().ConAlcanceDelSistemaSinSesion();

        var scopes = rutas.ServiceProvider.GetRequiredService<RegistroScopes>().Nombres.Order(StringComparer.Ordinal).ToArray();
        identidad.MapGet("/token/yo", (ClaimsPrincipal portador) => new PortadorToken(
                portador.GetClaim(Claims.Subject)!,
                portador.GetPresenters().FirstOrDefault() ?? portador.GetClaim(Claims.ClientId),
                portador.GetClaim(ClaimsAtlas.TipoPrincipal) ?? string.Empty,
                Guid.TryParse(portador.GetClaim(ClaimsAtlas.PersonaId), out var persona) ? persona : null,
                Guid.TryParse(portador.GetClaim(ClaimsAtlas.OrganizacionId), out var organizacion) ? organizacion : null,
                [.. portador.GetScopes().Order(StringComparer.Ordinal)],
                [.. portador.GetAudiences().Order(StringComparer.Ordinal)],
                portador.GetExpirationDate()))
            .RequiereScope(scopes)
            .WithName("ObtenerPortadorDelToken")
            .WithSummary("Portador del token de acceso")
            .WithDescription(
                "Para clientes que no son el navegador (atlas-cli, MCP, servicios internos): devuelve a quién representa " +
                "el token Bearer y con qué scopes y audiencias. Exige un token de Atlas con al menos un scope de módulo; " +
                "la cookie de la PWA no sirve aquí.")
            .Produces<PortadorToken>();
        return rutas;
    }

    /// <summary>
    /// <c>/connect/authorize</c>: el usuario que autoriza es el de la sesión del BFF. Sin sesión (o con
    /// <c>prompt=login</c> o <c>max_age</c> vencido) se pide el ingreso con Microsoft y se vuelve aquí.
    /// </summary>
    private static async Task<IResult> AutorizarAsync(
        HttpContext http, IOptions<OpcionesTokens> opciones, ProveedoresHabilitados proveedores, UserManager<UsuarioAtlas> usuarios,
        PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var solicitud = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Falta la solicitud de OpenIddict.");
        if (!opciones.Value.Clientes.TryGetValue(solicitud.ClientId ?? string.Empty, out var cliente))
        {
            return Rechazar(Errors.InvalidClient, "El cliente no está registrado.");
        }

        var sesion = await http.AuthenticateAsync(ClaimsAtlas.EsquemaCookie);
        var ahora = reloj.GetUtcNow();
        var ingreso = sesion.Succeeded ? Reautenticacion.MomentoIngreso(sesion.Principal) : null;
        var vencido = solicitud.MaxAge is { } maximo && (ingreso is null || ahora - ingreso > TimeSpan.FromSeconds(maximo));
        if (!sesion.Succeeded || solicitud.HasPromptValue(PromptValues.Login) || vencido)
        {
            if (solicitud.HasPromptValue(PromptValues.None))
            {
                return Rechazar(Errors.LoginRequired, "No hay una sesión de Atlas en este navegador.");
            }
            return PedirIngreso(http, solicitud, proveedores, reautenticar: sesion.Succeeded);
        }

        var usuarioId = Guid.Parse(sesion.Principal!.FindFirstValue(ClaimsAtlas.Sub)!);
        var titular = await TitularAsync(usuarios, plataforma, usuarioId, ct);
        if (titular is null)
        {
            return Rechazar(Errors.AccessDenied, "La cuenta no está activa.");
        }

        var identidad = Identidad(titular.Value.Usuario, titular.Value.Persona, Permitidos(solicitud.GetScopes(), cliente),
            cliente, ahora, ingreso ?? ahora);
        http.Items[AuditoriaTokens.ClaveDatos] = DatosEventoSeguridad.DeSesion(sesion.Principal);
        return Results.SignIn(new ClaimsPrincipal(identidad), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Navegación hacia el ingreso. Al volver de Microsoft la cookie <c>SameSite=Strict</c> no viaja en la redirección
    /// (la cadena empezó en otro sitio): por eso se vuelve a <see cref="ServidorTokens.RutaContinuar"/>, que reenvía
    /// a <c>/connect/authorize</c> desde una página de Atlas, y esa navegación sí lleva la cookie.
    /// </summary>
    private static IResult PedirIngreso(HttpContext http, OpenIddictRequest solicitud, ProveedoresHabilitados proveedores, bool reautenticar)
    {
        // Sin prompt ni max_age: al volver ya hay un ingreso reciente y no debe pedirse otra vez.
        var consulta = Consulta(solicitud, excluir: [Parameters.Prompt, Parameters.MaxAge]);
        if (!proveedores.Microsoft)
        {
            // Solo local y dev sin Entra: el ingreso simulado es de la PWA.
            return Pagina(
                "Inicia sesión en Atlas",
                "Entra a Atlas en este navegador y vuelve a intentar.",
                ("Ir a Atlas", "/"),
                ("Reintentar", ServidorTokens.RutaAutorizar + consulta));
        }
        var propiedades = new AuthenticationProperties { RedirectUri = ServidorTokens.RutaContinuar + consulta };
        if (reautenticar)
        {
            propiedades.Items[IngresoEntra.ClaveReautenticar] = "1";
        }
        return Results.Challenge(propiedades, [ClaimsAtlas.EsquemaEntra]);
    }

    /// <summary>
    /// Rebote tras el ingreso: una página mínima de Atlas que navega a <c>/connect/authorize</c> con la misma consulta.
    /// El destino es fijo (no hay redirección abierta) y la página no tiene scripts.
    /// </summary>
    private static PaginaSinCache Continuar(HttpContext http) =>
        Pagina("Continuando…", "Volviendo a la aplicación que pidió el acceso.", null,
            ("Continuar", ServidorTokens.RutaAutorizar + http.Request.QueryString.Value), redirigir: true);

    /// <summary><c>/connect/token</c>: código, refresh y client_credentials.</summary>
    private static async Task<IResult> EmitirAsync(
        HttpContext http, IOptions<OpcionesTokens> opciones, UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma,
        IOpenIddictTokenManager tokens, TimeProvider reloj, CancellationToken ct)
    {
        var solicitud = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("Falta la solicitud de OpenIddict.");
        if (!opciones.Value.Clientes.TryGetValue(solicitud.ClientId ?? string.Empty, out var cliente))
        {
            return Rechazar(Errors.InvalidClient, "El cliente no está registrado.");
        }

        if (solicitud.IsClientCredentialsGrantType())
        {
            // OpenIddict ya autenticó el secreto y validó los scopes contra los permisos del cliente.
            var servicio = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
            servicio.SetClaim(Claims.Subject, solicitud.ClientId)
                .SetClaim(Claims.Name, cliente.Nombre ?? solicitud.ClientId)
                .SetClaim(ClaimsAtlas.TipoPrincipal, ClaimsAtlas.TipoServicio);
            servicio.SetScopes(Permitidos(solicitud.GetScopes(), cliente));
            servicio.SetResources(cliente.AudienciasEfectivas);
            servicio.SetDestinations(_ => [Destinations.AccessToken]);
            return Results.SignIn(new ClaimsPrincipal(servicio), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (!solicitud.IsAuthorizationCodeGrantType() && !solicitud.IsRefreshTokenGrantType())
        {
            return Rechazar(Errors.UnsupportedGrantType, "Flujo no soportado.");
        }

        // El código o el refresh ya los validó OpenIddict (firma, vigencia, cliente, PKCE y canje único).
        var resultado = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var original = resultado.Principal;
        if (original is null || !Guid.TryParse(original.GetClaim(Claims.Subject), out var usuarioId))
        {
            return Rechazar(Errors.InvalidGrant, "La concesión no es válida.");
        }

        var ahora = reloj.GetUtcNow();
        var titular = await TitularAsync(usuarios, plataforma, usuarioId, ct);
        var concedidoEn = long.TryParse(original.GetClaim(ClaimConcedidoEn), NumberStyles.Integer, CultureInfo.InvariantCulture, out var segundos)
            ? DateTimeOffset.FromUnixTimeSeconds(segundos)
            : (DateTimeOffset?)null;
        var motivo = titular is null ? "cuenta_inactiva"
            // El sello cambia al suspender, cambiar roles o cerrar todo desde la administración (ATLAS-ADR-0022).
            : !string.Equals(original.GetClaim(ClaimsAtlas.SelloSeguridad), titular.Value.Usuario.SecurityStamp, StringComparison.Ordinal) ? "sello_de_seguridad"
            : concedidoEn is null || ahora - concedidoEn > opciones.Value.DuracionMaximaConcesion ? "concesion_vencida"
            : null;
        if (motivo is not null)
        {
            if (original.GetAuthorizationId() is { } autorizacion)
            {
                await tokens.RevokeByAuthorizationIdAsync(autorizacion, ct);
            }
            return Rechazar(Errors.InvalidGrant, $"La concesión ya no es válida ({motivo}). Vuelve a iniciar sesión.");
        }

        var ingreso = long.TryParse(original.GetClaim(ClaimsAtlas.AuthTime), NumberStyles.Integer, CultureInfo.InvariantCulture, out var autenticado)
            ? DateTimeOffset.FromUnixTimeSeconds(autenticado)
            : concedidoEn!.Value;
        // Los claims se arman de nuevo con los datos vigentes; los scopes no crecen y se recortan si el cliente perdió alguno.
        var identidad = Identidad(titular!.Value.Usuario, titular.Value.Persona, Permitidos(original.GetScopes(), cliente),
            cliente, concedidoEn!.Value, ingreso);
        http.Items[AuditoriaTokens.ClaveDatos] = new DatosEventoSeguridad
        {
            UsuarioId = usuarioId,
            PersonaId = titular.Value.Persona.Id,
            OrganizacionId = titular.Value.Persona.OrganizacionId,
        };
        return Results.SignIn(new ClaimsPrincipal(identidad), properties: null, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    /// <summary><c>/connect/userinfo</c> mínimo: <c>sub</c>, y nombre y correo según los scopes.</summary>
    private static async Task<IResult> UserinfoAsync(HttpContext http, PlataformaDbContext plataforma, UserManager<UsuarioAtlas> usuarios, CancellationToken ct)
    {
        var resultado = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var portador = resultado.Principal;
        var sub = portador?.GetClaim(Claims.Subject);
        if (portador is null || sub is null)
        {
            return Results.Challenge(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }
        var respuesta = new Dictionary<string, object?>(StringComparer.Ordinal) { [Claims.Subject] = sub };
        if (Guid.TryParse(sub, out var usuarioId) && await TitularAsync(usuarios, plataforma, usuarioId, ct) is { } titular)
        {
            if (portador.HasScope(Scopes.Profile))
            {
                respuesta[Claims.Name] = titular.Persona.Nombre;
            }
            if (portador.HasScope(Scopes.Email))
            {
                respuesta[Claims.Email] = titular.Persona.Correo;
                respuesta[Claims.EmailVerified] = true;
            }
        }
        return Results.Ok(respuesta);
    }

    /// <summary>Usuario y persona activos; <c>null</c> si no existe, está bloqueado o la persona no está activa.</summary>
    private static async Task<(UsuarioAtlas Usuario, Persona Persona)?> TitularAsync(
        UserManager<UsuarioAtlas> usuarios, PlataformaDbContext plataforma, Guid usuarioId, CancellationToken ct)
    {
        var usuario = await usuarios.FindByIdAsync(usuarioId.ToString());
        if (usuario is null || await usuarios.IsLockedOutAsync(usuario))
        {
            return null;
        }
        var persona = await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == usuario.PersonaId, ct);
        return persona is { Estado: EstadoPersona.Activa } ? (usuario, persona) : null;
    }

    /// <summary>
    /// Identidad de un usuario en los tokens (ATLAS-ADR-0004 §5): <c>sub</c>, persona, tipo y organización, sin roles ni
    /// alcances (el recurso los evalúa en el servidor). El sello y la fecha de la concesión son privados: solo van en el
    /// código y el refresh, para revisarlos en cada canje.
    /// </summary>
    private static ClaimsIdentity Identidad(
        UsuarioAtlas usuario, Persona persona, IEnumerable<string> scopes, ClienteTokens cliente, DateTimeOffset concedidoEn,
        DateTimeOffset ingreso)
    {
        var identidad = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, Claims.Name, Claims.Role);
        identidad.SetClaim(Claims.Subject, usuario.Id.ToString())
            .SetClaim(Claims.Name, persona.Nombre)
            .SetClaim(Claims.Email, persona.Correo)
            .SetClaim(ClaimsAtlas.PersonaId, persona.Id.ToString())
            .SetClaim(ClaimsAtlas.TipoPrincipal, persona.Tipo == TipoPersona.Externo ? ClaimsAtlas.TipoExterno : ClaimsAtlas.TipoEmpleado)
            .SetClaim(ClaimsAtlas.OrganizacionId, persona.OrganizacionId.ToString())
            .SetClaim(ClaimsAtlas.SelloSeguridad, usuario.SecurityStamp)
            .SetClaim(ClaimConcedidoEn, concedidoEn.ToUnixTimeSeconds())
            .SetClaim(ClaimsAtlas.AuthTime, ingreso.ToUnixTimeSeconds());
        identidad.SetScopes(scopes);
        identidad.SetResources(cliente.AudienciasEfectivas);
        identidad.SetDestinations(c => Destinos(c, identidad));
        return identidad;
    }

    private static IEnumerable<string> Destinos(Claim claim, ClaimsIdentity identidad) => claim.Type switch
    {
        Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Name when identidad.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Name => [Destinations.AccessToken],
        Claims.Email when identidad.HasScope(Scopes.Email) => [Destinations.IdentityToken],
        ClaimsAtlas.PersonaId or ClaimsAtlas.TipoPrincipal or ClaimsAtlas.OrganizacionId => [Destinations.AccessToken],
        ClaimsAtlas.AuthTime => [Destinations.IdentityToken],
        // Sello, fecha de la concesión y correo sin el scope email: privados (no van en tokens de acceso ni de identidad).
        _ => [],
    };

    /// <summary>Scopes pedidos que el cliente tiene permitidos hoy (OpenIddict ya rechazó los demás al pedirlos).</summary>
    private static IEnumerable<string> Permitidos(IEnumerable<string> pedidos, ClienteTokens cliente) =>
        pedidos.Where(s => s == Scopes.OpenId || cliente.Scopes.Contains(s)).Distinct(StringComparer.Ordinal);

    private static IResult Rechazar(string error, string descripcion) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = descripcion,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static string Consulta(OpenIddictRequest solicitud, string[] excluir) =>
        QueryString.Create(solicitud.GetParameters()
            .Where(p => !excluir.Contains(p.Key, StringComparer.Ordinal))
            .Select(p => new KeyValuePair<string, string?>(p.Key, (string?)p.Value))).Value ?? string.Empty;

    /// <summary>Página HTML mínima, sin scripts ni recursos externos.</summary>
    private static PaginaSinCache Pagina(string titulo, string texto, (string Texto, string Ruta)? secundario, (string Texto, string Ruta) principal,
        bool redirigir = false)
    {
        var html = HtmlEncoder.Default;
        var destino = html.Encode(principal.Ruta);
        var refresco = redirigir ? $"<meta http-equiv=\"refresh\" content=\"0;url={destino}\">" : string.Empty;
        var enlaceSecundario = secundario is { } s ? $"<a href=\"{html.Encode(s.Ruta)}\">{html.Encode(s.Texto)}</a> · " : string.Empty;
        var cuerpo = $"""
            <!doctype html>
            <html lang="es-CO">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">{refresco}<title>{html.Encode(titulo)} · Flit Atlas</title></head>
            <body><h1>{html.Encode(titulo)}</h1><p>{html.Encode(texto)}</p><p>{enlaceSecundario}<a href="{destino}">{html.Encode(principal.Texto)}</a></p></body>
            </html>
            """;
        return new PaginaSinCache(cuerpo);
    }

    private sealed class PaginaSinCache(string html) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            var cabeceras = httpContext.Response.Headers;
            cabeceras.CacheControl = "no-store";
            cabeceras["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            cabeceras["Referrer-Policy"] = "no-referrer";
            return Results.Content(html, "text/html; charset=utf-8").ExecuteAsync(httpContext);
        }
    }
}
