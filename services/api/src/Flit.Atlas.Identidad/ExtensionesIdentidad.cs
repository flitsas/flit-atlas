using System.Security.Cryptography.X509Certificates;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.ServiceDefaults;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Identity.Web;
using StackExchange.Redis;

namespace Flit.Atlas.Identidad;

/// <summary>Qué botones de ingreso ofrece este ambiente.</summary>
public sealed record ProveedoresHabilitados(bool Microsoft, bool Simulado);

public static class ExtensionesIdentidad
{
    public const string ClaveSimulado = "Identidad:Simulado:Habilitado";
    public const string SeccionEntra = "Identidad:Entra";
    public const string ClaveCertificadoProteccion = "Identidad:ProteccionDatos:Certificado";
    public const string ClaveClaveCertificadoProteccion = "Identidad:ProteccionDatos:ClaveCertificado";

    /// <summary>Ambientes donde se permite el proveedor simulado (HU 1.3: nunca en qa ni pdn).</summary>
    private static readonly string[] AmbientesConSimulado = ["local", "dev"];

    /// <summary>Ambientes donde Entra ID y la protección con certificado son obligatorios.</summary>
    private static readonly string[] AmbientesEstrictos = ["qa", "pdn"];

    /// <summary>
    /// Identidad de Atlas para empleados (HU 1.3): ASP.NET Core Identity, cookie del BFF con sesión en el servidor,
    /// Data Protection compartida en Redis, «Iniciar sesión con Microsoft» y, solo en local y dev, el proveedor simulado.
    /// </summary>
    public static WebApplicationBuilder AgregarIdentidad(this WebApplicationBuilder builder, string ambiente, string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var configuracion = builder.Configuration;
        var servicios = builder.Services;

        var simulado = configuracion.GetValue<bool>(ClaveSimulado);
        if (simulado && !AmbientesConSimulado.Contains(ambiente))
        {
            throw new InvalidOperationException(
                $"El proveedor simulado ({ConfiguracionRequerida.ComoVariableDeEntorno(ClaveSimulado)}) solo se permite en " +
                $"los ambientes local y dev; este es «{ambiente}». Quita la variable.");
        }

        var entra = configuracion.GetSection(SeccionEntra);
        var entraHabilitado = !string.IsNullOrWhiteSpace(entra["TenantId"]);
        if (entraHabilitado || AmbientesEstrictos.Contains(ambiente))
        {
            ConfiguracionRequerida.Validar(configuracion, $"{SeccionEntra}:TenantId", $"{SeccionEntra}:ClientId");
        }

        ConfigurarProteccionDeDatos(builder, ambiente);

        servicios.AddDbContext<IdentidadDbContext>(o => IdentidadDbContext.Configurar(o, cadenaConexion));
        servicios
            .AddIdentityCore<UsuarioAtlas>(IdentidadDbContext.ConfigurarIdentity)
            .AddEntityFrameworkStores<IdentidadDbContext>();

        servicios.TryAddSingleton(TimeProvider.System);
        servicios.AddSingleton<OpcionesSesion>();
        servicios.AddSingleton<AlmacenSesiones>();
        servicios.AddScoped<AprovisionadorEmpleados>();
        servicios.AddSingleton(new ProveedoresHabilitados(entraHabilitado, simulado));

        var autenticacion = servicios
            .AddAuthentication(o =>
            {
                o.DefaultScheme = ClaimsAtlas.EsquemaCookie;
                o.DefaultChallengeScheme = ClaimsAtlas.EsquemaCookie;
            })
            .AddCookie(ClaimsAtlas.EsquemaCookie, ConfigurarCookie);
        servicios.AddOptions<CookieAuthenticationOptions>(ClaimsAtlas.EsquemaCookie)
            .Configure<AlmacenSesiones>((o, almacen) => o.SessionStore = almacen);

        if (entraHabilitado)
        {
            ConfigurarEntra(builder, autenticacion, Guid.Parse(entra["TenantId"]!));
        }
        else
        {
            servicios.AddSingleton<IPerfilEmpleados, SinPerfilEmpleados>();
        }

        servicios.AddAuthorization();
        return builder;
    }

    private static void ConfigurarCookie(CookieAuthenticationOptions o)
    {
        // ATLAS-ADR-0008: __Host-, HttpOnly, Secure, SameSite=Strict y Path=/. Solo lleva el id de la sesión.
        o.Cookie.Name = ClaimsAtlas.NombreCookie;
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
        o.ExpireTimeSpan = new OpcionesSesion().DeslizanteEmpleados;
        o.SlidingExpiration = true;

        // La API responde códigos HTTP; la PWA decide a dónde llevar al usuario.
        o.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    private static void ConfigurarProteccionDeDatos(WebApplicationBuilder builder, string ambiente)
    {
        var proteccion = builder.Services.AddDataProtection().SetApplicationName("flit-atlas");

        // Llaves compartidas entre réplicas en Redis (ATLAS-ADR-0004 §7). El IConnectionMultiplexer lo registra
        // AddRedisClient en atlas-api.
        builder.Services.AddOptions<KeyManagementOptions>().Configure<IServiceProvider>((o, sp) =>
            o.XmlRepository = new RedisXmlRepository(
                () => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase(), "atlas:proteccion-datos:llaves"));

        var rutaCertificado = builder.Configuration[ClaveCertificadoProteccion];
        if (!string.IsNullOrWhiteSpace(rutaCertificado))
        {
            proteccion.ProtectKeysWithCertificate(X509CertificateLoader.LoadPkcs12FromFile(
                rutaCertificado, builder.Configuration[ClaveClaveCertificadoProteccion]));
        }
        else if (AmbientesEstrictos.Contains(ambiente))
        {
            // En qa y pdn las llaves no quedan en claro en Redis.
            ConfiguracionRequerida.Validar(builder.Configuration, ClaveCertificadoProteccion);
        }
    }

    private static void ConfigurarEntra(WebApplicationBuilder builder, AuthenticationBuilder autenticacion, Guid tenantFlit)
    {
        var entra = builder.Configuration.GetSection(SeccionEntra);

        autenticacion
            .AddMicrosoftIdentityWebApp(
                o =>
                {
                    entra.Bind(o);
                    o.Instance = string.IsNullOrWhiteSpace(o.Instance) ? "https://login.microsoftonline.com/" : o.Instance;
                    o.CallbackPath = EndpointsIdentidad.RutaRetornoEntra;
                    o.ResponseType = "code";
                    o.UsePkce = true;
                    o.SignInScheme = ClaimsAtlas.EsquemaCookie;
                },
                openIdConnectScheme: ClaimsAtlas.EsquemaEntra,
                cookieScheme: null)
            // MSAL canjea el código con la credencial de la app (certificado o secreto) y obtiene el token de
            // aplicación para Graph. Los tokens de Microsoft no salen del servidor ni se guardan en la base.
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();

        builder.Services.AddHttpClient<IPerfilEmpleados, PerfilGraph>(c => c.BaseAddress = new Uri("https://graph.microsoft.com/"));
        builder.Services.Configure<OpenIdConnectOptions>(ClaimsAtlas.EsquemaEntra, o =>
        {
            var previo = o.Events.OnTokenValidated;
            o.Events.OnTokenValidated = async ctx =>
            {
                await previo(ctx).ConfigureAwait(false);
                await IngresoEntra.AlValidarTokenAsync(ctx, tenantFlit).ConfigureAwait(false);
            };
            o.Events.OnRemoteFailure = IngresoEntra.AlFallarAsync;
        });
    }
}
