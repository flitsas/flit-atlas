// AppHost de Flit Atlas para desarrollo local: `dotnet run` en esta carpeta levanta todo.
// Puertos de los servicios: propuesta F0.3 (docs/preparacion/03-puertos-y-namespaces.md).
// Las dependencias (Postgres, Redis, RabbitMQ, S3) toman puertos dinámicos para no chocar con FLIT 2.0.

var builder = DistributedApplication.CreateBuilder(args);

// --- Dependencias ---

// Postgres 16 con pgvector, como en las VPS. El script de init hace lo que en dev/qa/pdn hace el administrador:
// crear la base y las extensiones vector y unaccent (prompt maestro A-06).
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "0.8.7-pg16")
    .WithInitFiles("./postgres-init")
    .WithDataVolume("flit-atlas-postgres");
var atlasDb = postgres.AddDatabase("atlas")
    // La base ya la creó el script de init; esto evita que Aspire intente crearla otra vez.
    .WithCreationScript("SELECT 1;");

var redis = builder.AddRedis("redis");
var rabbitmq = builder.AddRabbitMQ("rabbitmq");
// S3 local con RustFS (compatible con la API de MinIO). MinIO dejó de publicar imágenes públicas en Docker Hub
// y quay.io (revisado el 21/10/2026). La clave se genera y queda en los user secrets del AppHost.
var s3Usuario = builder.AddParameter("s3-usuario", "atlas-local");
var s3Clave = builder.AddParameter("s3-clave", new GenerateParameterDefault { MinLength = 24, Special = false }, secret: true, persist: true);
var s3 = builder.AddContainer("s3", "rustfs/rustfs", "1.0.1")
    .WithHttpEndpoint(targetPort: 9000, name: "http")
    .WithEnvironment("RUSTFS_ACCESS_KEY", s3Usuario)
    .WithEnvironment("RUSTFS_SECRET_KEY", s3Clave)
    .WithVolume("flit-atlas-s3", "/data")
    .WithHttpHealthCheck("/health/ready");

// Servidor SMTP de pruebas, solo local (HU 1.5): Mailpit (MIT), imagen fijada por digest. Los correos de invitación y
// recuperación se ven en http://localhost:4038 y las pruebas E2E los leen de su API. En qa y pdn el correo sale por Graph
// desde el buzón compartido (o por el relé SMTP que defina el líder).
var correo = builder.AddContainer("correo", "axllent/mailpit", "v1.31.4")
    .WithImageSHA256("b68349e3a014b90c5610bfb26b2ae36f3892d7b8cf25ee140c6c71c98d2fcf48")
    .WithEndpoint(targetPort: 1025, name: "smtp", scheme: "tcp")
    .WithHttpEndpoint(port: 4038, targetPort: 8025, name: "http")
    .WithHttpHealthCheck("/livez", endpointName: "http");
var smtp = correo.GetEndpoint("smtp");

// --- Servicios de Atlas ---

// CommunityToolkit marca AddGolangApp como obsoleto a favor de Aspire.Hosting.Go, que aún no está publicado
// en NuGet (revisado el 21/10/2026). Migrar a AddGoApp cuando exista una versión estable.
#pragma warning disable CS0618
var render = builder.AddGolangApp("render", "../../services/render", "./cmd/atlas-render")
    .WithHttpEndpoint(port: 4033, env: "ATLAS_RENDER_PORT")
    .WithHttpHealthCheck("/health/ready");
#pragma warning restore CS0618

var ia = builder.AddUvicornApp("ia", "../../services/ia", "atlas_ia.main:crear_app")
    .WithArgs("--factory")
    .WithUv()
    .WithEndpoint("http", e => e.Port = 4034)
    .WithEnvironment("ATLAS_AMBIENTE", "local")
    .WithEnvironment("ATLAS_IA_DATABASE_URL", atlasDb.Resource.UriExpression)
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(atlasDb);

var docs = builder.AddUvicornApp("docs", "../../services/docs", "atlas_docs.main:crear_app")
    .WithArgs("--factory")
    .WithUv()
    .WithEndpoint("http", e => e.Port = 4035)
    .WithEnvironment("ATLAS_AMBIENTE", "local")
    .WithEnvironment("ATLAS_DOCS_S3_ENDPOINT", s3.GetEndpoint("http"))
    .WithEnvironment("ATLAS_DOCS_S3_ACCESS_KEY", s3Usuario)
    .WithEnvironment("ATLAS_DOCS_S3_SECRET_KEY", s3Clave)
    .WithEnvironment("ATLAS_DOCS_S3_BUCKET", "atlas")
    // Solo en local: en dev/qa/pdn el bucket lo crea infraestructura (F0.4, ítems 4.1 y 4.2).
    .WithEnvironment("ATLAS_DOCS_S3_CREAR_BUCKET", "true")
    // HU 4.5 (ATLAS-ADR-0042): atlas-docs consume atlas.contratos.exportacion_solicitada (cola atlas.docs.exportaciones) y
    // publica los resultados en flit.atlas.docs. POST /exportes queda cerrado en local (sin JWKS, ATLAS-ADR-0044).
    .WithEnvironment("ATLAS_DOCS_RABBITMQ_URL", rabbitmq.Resource.ConnectionStringExpression)
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(s3)
    .WaitFor(rabbitmq);

// Aplica las migraciones y termina; atlas-api arranca solo si terminó bien.
var migrador = builder.AddProject<Projects.Flit_Atlas_Migrador>("migrador")
    .WithReference(atlasDb).WaitFor(atlasDb);

var api = builder.AddProject<Projects.Flit_Atlas_Api>("api")
    .WaitForCompletion(migrador)
    .WithEnvironment("Atlas__Ambiente", "local")
    // Proveedor simulado de empleados: solo local y dev (HU 1.3). atlas-api no arranca con él en qa ni pdn.
    .WithEnvironment("Identidad__Simulado__Habilitado", "true")
    // HU 1.5: el ingreso simulado puede pedir el grupo de administración (superadmin) para probar invitaciones; los
    // correos van a Mailpit; los vencimientos se procesan cada 10 s y los límites de ingreso admiten los tres perfiles E2E en paralelo desde la misma IP.
    .WithEnvironment("Identidad__Simulado__GrupoAdministracion", "5a1d0000-0000-4000-8000-00000000a7a5")
    .WithEnvironment("Atlas__UrlPublica", "https://localhost:4032")
    .WithEnvironment("Plataforma__Correo__Proveedor", "smtp")
    .WithEnvironment("Plataforma__Correo__Remitente", "atlas@ejemplo.com")
    .WithEnvironment("Plataforma__Correo__Smtp__Host", smtp.Property(EndpointProperty.Host))
    .WithEnvironment("Plataforma__Correo__Smtp__Puerto", smtp.Property(EndpointProperty.Port))
    .WithEnvironment("Plataforma__Correo__Smtp__Seguridad", "ninguna")
    .WithEnvironment("Identidad__Vencimientos__Intervalo", "00:00:10")
    .WithEnvironment("Identidad__LimiteTasa__ExternosPorIp", "120")
    .WithEnvironment("Identidad__LimiteTasa__IngresoPorIp", "120")
    .WithEnvironment("Identidad__LimiteTasa__PorIp", "1000")
    // HU 4.5: el mismo S3 local para la entrada de las exportaciones y las URL firmadas de descarga (el navegador llega al
    // mismo localhost). En qa y pdn, EndpointPublico es el host del S3 que alcanza el navegador.
    .WithEnvironment("Plataforma__Almacenamiento__Endpoint", s3.GetEndpoint("http"))
    .WithEnvironment("Plataforma__Almacenamiento__Usuario", s3Usuario)
    .WithEnvironment("Plataforma__Almacenamiento__Clave", s3Clave)
    .WithEnvironment("Plataforma__Almacenamiento__Bucket", "atlas")
    .WithEnvironment("Plataforma__Almacenamiento__CrearBucket", "true")
    .WaitFor(s3)
    .WaitFor(correo)
    .WithReference(atlasDb).WaitFor(atlasDb)
    .WithReference(redis).WaitFor(redis)
    .WithReference(rabbitmq).WaitFor(rabbitmq)
    .WithReference(render)
    .WithReference(ia)
    .WithReference(docs)
    .WithHttpHealthCheck("/health/ready");

// «Iniciar sesión con Microsoft» en local, con el registro de Entra ID del ambiente dev. Los valores van en los user
// secrets del AppHost (nunca en el repo); sin ellos, solo queda el proveedor simulado. Ver aspire/README.md.
var entra = builder.Configuration.GetSection("Entra");
if (!string.IsNullOrWhiteSpace(entra["TenantId"]))
{
    api.WithEnvironment("Identidad__Entra__TenantId", entra["TenantId"])
        .WithEnvironment("Identidad__Entra__ClientId", entra["ClientId"]);

    if (!string.IsNullOrWhiteSpace(entra["CertificadoRuta"]))
    {
        // Certificado de cliente (recomendado, F0.4 ítem 5.2): archivo .pfx local y su clave.
        var claveCertificado = builder.AddParameter("entra-clave-certificado", secret: true);
        api.WithEnvironment("Identidad__Entra__ClientCredentials__0__SourceType", "Path")
            .WithEnvironment("Identidad__Entra__ClientCredentials__0__CertificateDiskPath", entra["CertificadoRuta"])
            .WithEnvironment("Identidad__Entra__ClientCredentials__0__CertificatePassword", claveCertificado);
    }
    else
    {
        var secreto = builder.AddParameter("entra-secreto", secret: true);
        api.WithEnvironment("Identidad__Entra__ClientCredentials__0__SourceType", "ClientSecret")
            .WithEnvironment("Identidad__Entra__ClientCredentials__0__ClientSecret", secreto);
    }
}

// Web Push (HU 1.6): llaves VAPID en los user secrets del AppHost (node scripts/generar-vapid.mjs). Sin ellas, atlas-api
// genera unas efímeras en cada arranque y la PWA renueva sola la suscripción del dispositivo.
var vapid = builder.Configuration.GetSection("Vapid");
if (!string.IsNullOrWhiteSpace(vapid["ClavePublica"]))
{
    var vapidPrivada = builder.AddParameter("vapid-clave-privada", secret: true);
    api.WithEnvironment("Plataforma__Push__ClavePublica", vapid["ClavePublica"])
        .WithEnvironment("Plataforma__Push__ClavePrivada", vapidPrivada);
}

// Servidor de tokens (HU 1.3c, ATLAS-ADR-0024): clientes confidenciales de servicio solo para tu máquina (atlas-cli y
// atlas-mcp ya vienen en el appsettings de atlas-api). La definición va en «Tokens:Clientes:<id>» de los user secrets
// del AppHost y el secreto en «Parameters:token-<id>-secreto»; nunca en el repo. Ver aspire/README.md. Sin certificados,
// atlas-api usa llaves de firma y cifrado efímeras: los tokens dejan de valer al reiniciar.
foreach (var cliente in builder.Configuration.GetSection("Tokens:Clientes").GetChildren())
{
    foreach (var (clave, valor) in Microsoft.Extensions.Configuration.ConfigurationExtensions.AsEnumerable(cliente, makePathsRelative: true))
    {
        if (valor is not null && clave != "Secreto")
        {
            api.WithEnvironment($"Identidad__Tokens__Clientes__{cliente.Key}__{clave.Replace(":", "__", StringComparison.Ordinal)}", valor);
        }
    }
    var secreto = builder.AddParameter($"token-{cliente.Key}-secreto", secret: true);
    api.WithEnvironment($"Identidad__Tokens__Clientes__{cliente.Key}__Secreto", secreto);
}

// ASPIRECERTIFICATES001: las APIs de certificados de Aspire son experimentales; solo afectan el entorno local.
#pragma warning disable ASPIRECERTIFICATES001
builder.AddViteApp("web", "../../apps/web")
    .WithPnpm()
    // HTTPS también en local, con el certificado de desarrollo de .NET (dotnet dev-certs https --trust): la cookie
    // __Host-atlas es Secure y WebKit (iOS) no la guarda en http. Vite recibe las rutas del certificado y la llave.
    .WithEndpoint("http", e =>
    {
        e.UriScheme = "https";
        e.Port = 4032;
    })
    .WithHttpsDeveloperCertificate()
    .WithHttpsCertificateConfiguration(ctx =>
    {
        ctx.EnvironmentVariables["ATLAS_WEB_CERTIFICADO"] = ctx.CertificatePath;
        ctx.EnvironmentVariables["ATLAS_WEB_LLAVE"] = ctx.KeyPath;
        return Task.CompletedTask;
    })
    // La PWA y la API comparten origen (cookie SameSite=Strict, ATLAS-ADR-0008): Vite hace de proxy hacia la API.
    .WithEnvironment("ATLAS_API_URL", api.GetEndpoint("http"))
    .WithHttpHealthCheck("/")
    .WaitFor(api);
#pragma warning restore ASPIRECERTIFICATES001

builder.Build().Run();
