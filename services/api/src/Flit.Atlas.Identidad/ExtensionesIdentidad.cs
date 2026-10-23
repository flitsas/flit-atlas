using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Emergencia;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Personas;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Identidad.Tokens;
using Flit.Atlas.Plataforma.Correo;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Permisos;
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
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using StackExchange.Redis;

namespace Flit.Atlas.Identidad;

/// <summary>Qué botones de ingreso ofrece este ambiente.</summary>
[Description("Proveedores de ingreso que ofrece este ambiente. El simulado solo existe en local y dev.")]
public sealed record ProveedoresHabilitados(bool Microsoft, bool Simulado);

public static class ExtensionesIdentidad
{
    public const string ClaveSimulado = "Identidad:Simulado:Habilitado";

    /// <summary>Solo local y dev: grupo simulado que da atlas.superadmin (lo fija el AppHost para probar la administración).</summary>
    public const string ClaveSimuladoGrupoAdministracion = "Identidad:Simulado:GrupoAdministracion";
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
    /// Sesiones y seguridad (HU 1.3b): sello de seguridad, reautenticación, bloqueo progresivo, límite de tasa y eventos
    /// de seguridad. En el pipeline, <see cref="UseSeguridadIdentidad"/> va antes de <c>UseAuthentication</c>.
    /// Servidor de tokens (HU 1.3c): OpenIddict para los clientes que no son el navegador (ATLAS-ADR-0024).
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
            .AddEntityFrameworkStores<IdentidadDbContext>()
            // HU 1.5 (ATLAS-ADR-0025): clave TOTP cifrada y códigos de recuperación con hash; TOTP y enlaces de
            // recuperación de Identity; contraseñas revisadas contra filtraciones.
            .AddUserStore<AlmacenUsuarios>()
            .AddTokenProvider<DataProtectorTokenProvider<UsuarioAtlas>>(TokenOptions.DefaultProvider)
            .AddTokenProvider<AuthenticatorTokenProvider<UsuarioAtlas>>(TokenOptions.DefaultAuthenticatorProvider)
            // ATLAS-ADR-0027: enlace de activación de la cuenta de emergencia, con su propio protector y vigencia.
            .AddTokenProvider<ProveedorTokenEmergencia>(CuentaEmergencia.ProveedorToken)
            .AddPasswordValidator<ValidadorContrasenaFiltrada>();
        AgregarCuentasExternas(builder, ambiente, entraHabilitado);

        servicios.TryAddSingleton(TimeProvider.System);
        servicios.AddSingleton(configuracion.GetSection(OpcionesSesion.Seccion).Get<OpcionesSesion>() ?? new OpcionesSesion());
        servicios.AddSingleton<AlmacenSesiones>();
        servicios.AddScoped<AprovisionadorEmpleados>();
        // HU 1.3b (ATLAS-ADR-0022): eventos de seguridad, sello de seguridad, bloqueo progresivo y límite de tasa.
        servicios.AddHttpContextAccessor();
        servicios.AddScoped<RegistroSeguridad>();
        servicios.AddScoped<IInvalidadorSesiones, InvalidadorSesiones>();
        servicios.AddScoped<VerificadorContrasena>();
        LimitacionTasa.Agregar(servicios);
        servicios.AddSingleton(new ProveedoresHabilitados(entraHabilitado, simulado));
        AgregarAutorizacion(servicios);

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
        AgregarSincronizacionPersonas(builder, entraHabilitado);

        PoliticasIdentidad.Configurar(servicios.AddAuthorizationBuilder());

        // HU 1.3c (ATLAS-ADR-0024): OpenIddict sobre IdentidadDbContext, con su esquema de validación para los Bearer.
        ServidorTokens.Agregar(builder, AmbientesEstrictos.Contains(ambiente));
        return builder;
    }

    /// <summary>
    /// Roles y permisos (HU 1.4, ATLAS-ADR-0023): manifiesto de la identidad, permisos efectivos de la sesión calculados en
    /// el servidor en cada petición, alcance de datos de la sesión (predicado obligatorio y RLS), administración de roles y
    /// el vigilante de vencimientos.
    /// </summary>
    private static void AgregarAutorizacion(IServiceCollection servicios)
    {
        servicios.AgregarManifiestoPermisos(PermisosIdentidad.Manifiesto);
        servicios.AddScoped<IPermisosDeSesion, PermisosDeSesion>();
        servicios.Replace(ServiceDescriptor.Scoped<IAlcanceDatos, AlcanceDatosDeSesion>());
        servicios.AddScoped<AdministracionRoles>();
        servicios.AddOptions<OpcionesVencimientos>().BindConfiguration(OpcionesVencimientos.Seccion);
        servicios.AddSingleton<VigilanteVencimientos>();
        servicios.AddHostedService(sp => sp.GetRequiredService<VigilanteVencimientos>());
    }

    /// <summary>
    /// Sincronización diaria de personas con Graph (HU 2.2, ATLAS-ADR-0030): el directorio de Entra (solo con Entra
    /// configurado), la corrida y el trabajo programado, que también atiende «Sincronizar ahora».
    /// </summary>
    private static void AgregarSincronizacionPersonas(WebApplicationBuilder builder, bool entraHabilitado)
    {
        var servicios = builder.Services;
        var seccion = builder.Configuration.GetSection(OpcionesSincronizacionPersonas.Seccion);
        if (entraHabilitado)
        {
            // Falla al arrancar con una configuración imposible (p. ej. origen «grupo» sin grupo).
            (seccion.Get<OpcionesSincronizacionPersonas>() ?? new OpcionesSincronizacionPersonas()).Validar();
            servicios.AddHttpClient<IDirectorioEntra, DirectorioGraph>(c => c.BaseAddress = new Uri("https://graph.microsoft.com/"));
        }
        else
        {
            servicios.AddSingleton<IDirectorioEntra, SinDirectorioEntra>();
        }
        servicios.AddOptions<OpcionesSincronizacionPersonas>().Bind(seccion);
        servicios.AddScoped<SincronizadorPersonas>();
        servicios.AddSingleton<TrabajoSincronizacionPersonas>();
        servicios.AddHostedService(sp => sp.GetRequiredService<TrabajoSincronizacionPersonas>());
    }

    /// <summary>
    /// Organizaciones, invitaciones y cuentas locales de externos (HU 1.5, ATLAS-ADR-0025): passkeys nativas de Identity,
    /// pasos temporales en Redis, correo (Graph o SMTP), documentos legales y la sesión de externos. En qa y pdn exige el
    /// correo y la URL pública con la que se arman los enlaces.
    /// </summary>
    private static void AgregarCuentasExternas(WebApplicationBuilder builder, string ambiente, bool entraHabilitado)
    {
        var configuracion = builder.Configuration;
        var servicios = builder.Services;
        var proveedorCorreo = configuracion[$"{OpcionesCorreo.Seccion}:Proveedor"]?.Trim().ToLowerInvariant();
        if (AmbientesEstrictos.Contains(ambiente))
        {
            ConfiguracionRequerida.Validar(configuracion, OpcionesExternos.ClaveUrlPublica, $"{OpcionesCorreo.Seccion}:Proveedor", $"{OpcionesCorreo.Seccion}:Remitente");
        }
        if (proveedorCorreo == OpcionesCorreo.ProveedorSmtp)
        {
            ConfiguracionRequerida.Validar(configuracion, $"{OpcionesCorreo.Seccion}:Remitente", $"{OpcionesCorreo.Seccion}:Smtp:Host");
        }
        else if (proveedorCorreo == OpcionesCorreo.ProveedorGraph)
        {
            ConfiguracionRequerida.Validar(configuracion, $"{OpcionesCorreo.Seccion}:Remitente", $"{SeccionEntra}:TenantId", $"{SeccionEntra}:ClientId");
            if (entraHabilitado)
            {
                servicios.AddHttpClient<EnviadorGraph>(c => c.BaseAddress = new Uri("https://graph.microsoft.com/"));
                servicios.Replace(ServiceDescriptor.Transient<IEnviadorCorreo>(sp => sp.GetRequiredService<EnviadorGraph>()));
            }
        }
        else if (!string.IsNullOrEmpty(proveedorCorreo))
        {
            throw new InvalidOperationException(
                $"{ConfiguracionRequerida.ComoVariableDeEntorno($"{OpcionesCorreo.Seccion}:Proveedor")} inválido: «{proveedorCorreo}». Valores: graph, smtp.");
        }

        servicios.AddOptions<OpcionesExternos>().BindConfiguration(OpcionesExternos.Seccion);
        servicios.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<OpcionesExternos>>((o, externos) => o.TokenLifespan = externos.Value.VigenciaRecuperacion);
        // Passkeys (WebAuthn nativo de .NET 10): verificación de usuario y credencial descubrible obligatorias, para que
        // la passkey sea un factor fuerte y se pueda entrar sin escribir el correo.
        servicios.TryAddScoped<IPasskeyHandler<UsuarioAtlas>, PasskeyHandler<UsuarioAtlas>>();
        servicios.Configure<IdentityPasskeyOptions>(o =>
        {
            o.UserVerificationRequirement = "required";
            o.ResidentKeyRequirement = "required";
            o.ServerDomain = string.IsNullOrWhiteSpace(configuracion["Identidad:Passkeys:Dominio"]) ? null : configuracion["Identidad:Passkeys:Dominio"];
        });
        servicios.AddHttpClient(ValidadorContrasenaFiltrada.NombreCliente, c =>
        {
            c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("FlitAtlas/1.0");
        });
        servicios.AddSingleton<PasosTemporales>();
        servicios.AddScoped<CorreosExternos>();
        servicios.AddScoped<DocumentosLegales>();
        servicios.AddScoped<FactoresExternos>();
        servicios.AddScoped<SesionExterna>();
        servicios.AddScoped<CuentasExternas>();
        servicios.AddScoped<ServicioInvitaciones>();

        // Cuenta de administrador de emergencia (ATLAS-ADR-0027): reutiliza los factores y la sesión de las cuentas locales.
        servicios.AddOptions<OpcionesEmergencia>().BindConfiguration(OpcionesEmergencia.Seccion);
        servicios.AddOptions<OpcionesTokenEmergencia>()
            .Configure<IOptions<OpcionesEmergencia>>((o, emergencia) => o.TokenLifespan = emergencia.Value.VigenciaEnlace);
        servicios.AddScoped<CuentaEmergencia>();
    }

    /// <summary>
    /// Middleware de la HU 1.3b: limitación de tasa de las rutas de identidad y sesión nueva en cada ingreso. Va
    /// después de <c>UseForwardedHeaders</c> (usa la IP del cliente) y <b>antes</b> de <c>UseAuthentication</c> (cubre
    /// el retorno de Entra, que atiende el middleware de autenticación).
    /// </summary>
    public static IApplicationBuilder UseSeguridadIdentidad(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        LimitacionTasa.Usar(app);
        RenovacionAlIngresar.Usar(app);
        return app;
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
            var previoRedireccion = o.Events.OnRedirectToIdentityProvider;
            o.Events.OnRedirectToIdentityProvider = async ctx =>
            {
                await previoRedireccion(ctx).ConfigureAwait(false);
                await IngresoEntra.AlRedirigirAsync(ctx).ConfigureAwait(false);
            };
        });
    }
}
