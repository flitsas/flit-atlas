using System.Security.Cryptography.X509Certificates;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Flit.Atlas.Identidad.Tokens;

/// <summary>
/// Servidor de tokens de OpenIddict embebido en atlas-api (HU 1.3c, ATLAS-ADR-0024) para los clientes que no son el
/// navegador: <c>atlas-cli</c>, MCP (F7), el widget (F7) y servicios internos. La PWA no usa estos tokens: usa la
/// cookie del BFF (ATLAS-ADR-0008). Nada de criptografía propia: firma, cifrado, PKCE, rotación de refresh tokens y
/// detección de reutilización son de OpenIddict.
/// </summary>
public static class ServidorTokens
{
    public const string RutaAutorizar = "/connect/authorize";
    public const string RutaToken = "/connect/token";
    public const string RutaRevocar = "/connect/revoke";
    public const string RutaIntrospeccion = "/connect/introspect";
    public const string RutaUserinfo = "/connect/userinfo";

    /// <summary>Rebote propio tras el ingreso con Microsoft (ver <see cref="EndpointsTokens"/>).</summary>
    public const string RutaContinuar = "/connect/continuar";

    /// <summary>Descubrimiento y JWKS: las rutas por defecto de OpenIddict.</summary>
    public const string RutaDescubrimiento = "/.well-known/openid-configuration";
    public const string RutaJwks = "/.well-known/jwks";

    /// <param name="estricto">qa y pdn: los certificados de firma y cifrado son obligatorios.</param>
    internal static void Agregar(WebApplicationBuilder builder, bool estricto)
    {
        var configuracion = builder.Configuration;
        var servicios = builder.Services;
        // Se leen aquí para configurar OpenIddict; la validación completa (con los scopes de todos los módulos) corre al
        // arrancar el host.
        var opciones = configuracion.GetSection(OpcionesTokens.Seccion).Get<OpcionesTokens>() ?? new OpcionesTokens();

        servicios.AddOptions<OpcionesTokens>().BindConfiguration(OpcionesTokens.Seccion).ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<OpcionesTokens>, ValidadorOpcionesTokens>();
        servicios.AgregarScopes(new ScopesAtlas.Base());
        servicios.TryAddSingleton<RegistroScopes>();
        servicios.AddHostedService<SincronizadorClientes>();

        var firma = Certificados(configuracion, OpcionesTokens.ClaveCertificadosFirma);
        var cifrado = Certificados(configuracion, OpcionesTokens.ClaveCertificadosCifrado);
        if (estricto && (firma.Count == 0 || cifrado.Count == 0))
        {
            // En qa y pdn las llaves vienen de un SealedSecret montado como archivos; nunca se generan en la app.
            ConfiguracionRequerida.Validar(configuracion, OpcionesTokens.ClaveCertificadosFirma, OpcionesTokens.ClaveCertificadosCifrado);
        }

        servicios.AddOpenIddict()
            .AddCore(o => o.UseEntityFrameworkCore().UseDbContext<IdentidadDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(o =>
            {
                o.SetAuthorizationEndpointUris(Relativa(RutaAutorizar))
                    .SetTokenEndpointUris(Relativa(RutaToken))
                    .SetRevocationEndpointUris(Relativa(RutaRevocar))
                    .SetIntrospectionEndpointUris(Relativa(RutaIntrospeccion))
                    .SetUserInfoEndpointUris(Relativa(RutaUserinfo));

                o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()
                    .AllowRefreshTokenFlow()
                    .AllowClientCredentialsFlow();
                // PKCE solo con S256: «plain» no protege si el código se filtra junto con el reto.
                o.Configure(s => s.CodeChallengeMethods.Remove(CodeChallengeMethods.Plain));

                o.SetAccessTokenLifetime(opciones.DuracionAcceso)
                    .SetIdentityTokenLifetime(opciones.DuracionAcceso)
                    .SetAuthorizationCodeLifetime(opciones.DuracionCodigo)
                    .SetRefreshTokenLifetime(opciones.DuracionRefresh)
                    .SetRefreshTokenReuseLeeway(opciones.MargenReutilizacionRefresh);
                if (opciones.Emisor is not null)
                {
                    o.SetIssuer(opciones.Emisor);
                }

                // Los de Atlas llegan del registro de scopes (abajo); estos son los estándar de OIDC.
                o.RegisterScopes(Scopes.Email, Scopes.Profile, Scopes.OfflineAccess);

                if (firma.Count > 0)
                {
                    // Con varios, OpenIddict firma con el vigente que vence más tarde y publica todos en el JWKS: así se
                    // rota sin cortar (ATLAS-ADR-0024).
                    o.AddSigningCertificates(firma);
                }
                else
                {
                    o.AddEphemeralSigningKey();
                }
                if (cifrado.Count > 0)
                {
                    o.AddEncryptionCertificates(cifrado);
                }
                else
                {
                    o.AddEphemeralEncryptionKey();
                }

                o.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();

                o.AddEventHandler<ApplyTokenResponseContext>(b =>
                    b.UseScopedHandler<AuditoriaTokens>().SetOrder(int.MinValue + 100_000).SetType(OpenIddictServerHandlerType.Custom));
                o.AddEventHandler<ApplyAuthorizationResponseContext>(b =>
                    b.UseScopedHandler<AuditoriaTokens>().SetOrder(int.MinValue + 100_000).SetType(OpenIddictServerHandlerType.Custom));
                // Servicios internos (ATLAS-ADR-0044): sus tokens van firmados y sin cifrar para validarlos con el JWKS.
                o.AddEventHandler(TokensParaServicios.Descriptor);
            })
            .AddValidation(o =>
            {
                o.UseLocalServer();
                o.UseAspNetCore();
                o.AddAudiences(OpcionesTokens.AudienciaApi);
                // Cada petición con Bearer comprueba en la base que el token y su autorización sigan vigentes: revocar
                // (cierre, suspensión, reutilización de un refresh) corta el acceso de inmediato, como con las sesiones.
                o.EnableTokenEntryValidation();
                o.EnableAuthorizationEntryValidation();
            });

        servicios.AddOptions<OpenIddictServerOptions>().Configure<RegistroScopes>((o, registro) => o.Scopes.UnionWith(registro.Nombres));
    }

    private static Uri Relativa(string ruta) => new(ruta.TrimStart('/'), UriKind.Relative);

    /// <summary>Uno o más archivos .pfx separados por «;» (montados desde un SealedSecret en qa y pdn).</summary>
    internal static List<X509Certificate2> Certificados(IConfiguration configuracion, string clave)
    {
        var valor = configuracion[clave];
        if (string.IsNullOrWhiteSpace(valor))
        {
            return [];
        }
        var contrasena = configuracion[OpcionesTokens.ClaveClaveCertificados];
        var certificados = new List<X509Certificate2>();
        foreach (var ruta in valor.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!File.Exists(ruta))
            {
                throw new InvalidOperationException(
                    $"{ConfiguracionRequerida.ComoVariableDeEntorno(clave)} apunta a un archivo que no existe. Revisa el volumen montado.");
            }
            // La llave privada queda solo en memoria: no se escribe en el almacén de llaves del sistema.
            var certificado = X509CertificateLoader.LoadPkcs12FromFile(ruta, contrasena, X509KeyStorageFlags.EphemeralKeySet);
            if (!certificado.HasPrivateKey)
            {
                throw new InvalidOperationException($"El certificado de {ConfiguracionRequerida.ComoVariableDeEntorno(clave)} no trae la llave privada.");
            }
            certificados.Add(certificado);
        }
        return certificados;
    }
}

/// <summary>
/// Falla el arranque con todos los errores de <c>Identidad:Tokens</c> a la vez. También ata el registro de scopes al de
/// permisos (ATLAS-ADR-0023): cada scope pertenece a un módulo que tiene manifiesto de permisos.
/// </summary>
internal sealed class ValidadorOpcionesTokens(RegistroScopes scopes, CatalogoPermisos catalogo) : IValidateOptions<OpcionesTokens>
{
    public ValidateOptionsResult Validate(string? name, OpcionesTokens options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var modulos = catalogo.Manifiestos.Select(m => m.Modulo).ToHashSet(StringComparer.Ordinal);
        var errores = options.Errores(scopes)
            .Concat(scopes.Todos.Where(s => !modulos.Contains(s.Modulo))
                .Select(s => $"El scope «{s.Nombre}» es del módulo «{s.Modulo}», que no tiene manifiesto de permisos."))
            .ToList();
        return errores.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errores);
    }
}
