using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Modulos.Contratos.Specs;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.RabbitMq;

namespace Flit.Atlas.Api.Tests.Exportaciones;

/// <summary>
/// S3 de las pruebas de exportaciones (HU 4.5): RustFS, la misma imagen del AppHost (MinIO ya no publica imágenes
/// públicas), y RabbitMQ, en una red de Docker propia. Con <c>conDocs</c>, también atlas-docs construido desde
/// <c>services/docs/Dockerfile</c> (objetivo <c>docs</c>, con Pango): la prueba de punta a punta usa el generador real.
/// Las credenciales del S3 se generan al azar en cada corrida.
/// </summary>
public class ContenedoresExportaciones : IAsyncLifetime
{
    private readonly bool _conDocs;

    public ContenedoresExportaciones() => _conDocs = false;

    protected ContenedoresExportaciones(bool conDocs) => _conDocs = conDocs;

    public const string ImagenS3 = "rustfs/rustfs:1.0.1";
    public const string Bucket = "atlas";
    public const string Usuario = "atlas-pruebas";

    public string Clave { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    private INetwork? _red;
    private IContainer? _s3;
    private RabbitMqContainer? _rabbit;
    private IContainer? _docs;
    private IFutureDockerImage? _imagen;

    public string EndpointS3 => $"http://{_s3!.Hostname}:{_s3.GetMappedPublicPort(9000)}";

    public string RabbitMq => _rabbit!.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        _red = new NetworkBuilder().Build();
        await _red.CreateAsync();
        _s3 = new ContainerBuilder(ImagenS3)
            .WithNetwork(_red)
            .WithNetworkAliases("s3")
            .WithPortBinding(9000, assignRandomHostPort: true)
            .WithEnvironment("RUSTFS_ACCESS_KEY", Usuario)
            .WithEnvironment("RUSTFS_SECRET_KEY", Clave)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(9000).ForPath("/health/ready")))
            .Build();
        _rabbit = new RabbitMqBuilder("rabbitmq:4.3").WithNetwork(_red).WithNetworkAliases("rabbitmq").Build();
        await Task.WhenAll(_s3.StartAsync(), _rabbit.StartAsync());
        if (!_conDocs)
        {
            return;
        }

        _imagen = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(Repositorio.Raiz)
            .WithDockerfile("services/docs/Dockerfile")
            .WithTarget("docs")
            .WithName("flit-atlas/atlas-docs-pruebas:e2e")
            .WithCleanUp(false)
            .Build();
        await _imagen.CreateAsync();
        _docs = new ContainerBuilder(_imagen)
            .WithNetwork(_red)
            .WithPortBinding(4035, assignRandomHostPort: true)
            .WithEnvironment("ATLAS_AMBIENTE", "local")
            .WithEnvironment("ATLAS_DOCS_S3_ENDPOINT", "http://s3:9000")
            .WithEnvironment("ATLAS_DOCS_S3_ACCESS_KEY", Usuario)
            .WithEnvironment("ATLAS_DOCS_S3_SECRET_KEY", Clave)
            .WithEnvironment("ATLAS_DOCS_S3_BUCKET", Bucket)
            .WithEnvironment("ATLAS_DOCS_S3_CREAR_BUCKET", "true")
            .WithEnvironment("ATLAS_DOCS_RABBITMQ_URL", "amqp://rabbitmq:rabbitmq@rabbitmq:5672/")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(4035).ForPath("/health/ready")))
            .Build();
        await _docs.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        foreach (var contenedor in new IAsyncDisposable?[] { _docs, _rabbit, _s3 })
        {
            if (contenedor is not null)
            {
                await contenedor.DisposeAsync();
            }
        }
        if (_red is not null)
        {
            await _red.DisposeAsync();
        }
    }

    /// <summary>La configuración de atlas-api para este S3 (y, si se pide, este RabbitMQ con el consumo encendido).</summary>
    public Dictionary<string, string> Configuracion(bool conBroker, Dictionary<string, string>? extra = null)
    {
        var configuracion = new Dictionary<string, string>
        {
            ["Plataforma:Almacenamiento:Endpoint"] = EndpointS3,
            ["Plataforma:Almacenamiento:Usuario"] = Usuario,
            ["Plataforma:Almacenamiento:Clave"] = Clave,
            ["Plataforma:Almacenamiento:Bucket"] = Bucket,
            ["Plataforma:Almacenamiento:CrearBucket"] = "true",
        };
        if (conBroker)
        {
            configuracion["ConnectionStrings:rabbitmq"] = RabbitMq;
            configuracion["Plataforma:Mensajeria:Consumo:Habilitado"] = "true";
            configuracion["Plataforma:Mensajeria:Consumo:EsperaConexion"] = "00:00:01";
        }
        foreach (var (clave, valor) in extra ?? [])
        {
            configuracion[clave] = valor;
        }
        return configuracion;
    }
}

/// <summary>Con atlas-docs real (la prueba de punta a punta).</summary>
public sealed class ContenedoresConDocs : ContenedoresExportaciones
{
    public ContenedoresConDocs()
        : base(conDocs: true)
    {
    }
}

/// <summary>Un paquete sembrado en la base: el contrato, su versión, sus operaciones y el paquete publicado.</summary>
public sealed record PaqueteSembrado(Guid ContratoId, Guid VersionId, Guid PaqueteId, string Codigo, string Version, int Operaciones);

/// <summary>Datos de contratos de las pruebas de exportaciones, escritos directo en la base (lo de la CI lo prueban otras).</summary>
public static class ContratosDePrueba
{
    public static string RutaIct => Path.Combine(Repositorio.Raiz, "contracts", "ict", "core-ict.enriquecido.v1.yaml");

    public static ContratosDbContext Contexto(string cadena)
    {
        var opciones = new DbContextOptionsBuilder<ContratosDbContext>();
        ContratosDbContext.Configurar(opciones, cadena);
        return new ContratosDbContext(opciones.Options);
    }

    /// <summary>
    /// El contrato ICT enriquecido (compartible, 1.1.0) en un paquete publicado con todas sus operaciones compartibles y
    /// una guía compartible publicada. <paramref name="tipo"/> permite sembrar un proto (no exportable).
    /// </summary>
    public static async Task<PaqueteSembrado> SembrarAsync(string cadena, string? contenido = null, TipoContrato tipo = TipoContrato.Openapi, CancellationToken ct = default)
    {
        var spec = contenido ?? await File.ReadAllTextAsync(RutaIct, ct);
        var codigo = $"core-ict-{Guid.NewGuid().ToString("N")[..8]}";
        await using var contexto = Contexto(cadena);
        var contrato = new Contrato
        {
            AplicativoId = Guid.NewGuid(), Codigo = codigo, Tipo = tipo, Nombre = "FLIT Core ICT API", Visibilidad = VisibilidadContrato.Compartible,
            Repositorio = "flitsas-demo/core-ict",
        };
        var analizado = tipo == TipoContrato.Proto ? null : AnalizadorSpec.Analizar(tipo, spec);
        var version = new VersionContrato
        {
            ContratoId = contrato.Id, Version = "1.1.0", Mayor = 1, Menor = 1, Parche = 0, EsEstable = true,
            Sha256 = AnalizadorSpec.Sha256(spec), Formato = tipo == TipoContrato.Proto ? FormatoSpec.Proto : FormatoSpec.Yaml, Contenido = spec,
            Tamano = System.Text.Encoding.UTF8.GetByteCount(spec), Repositorio = "flitsas-demo/core-ict", Rama = "release",
            CommitSha = new string('a', 40), WorkflowDelJob = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/heads/develop",
            PublicadoPor = "github:flitsas-demo/core-ict@release", PublicadoEn = DateTimeOffset.UtcNow,
        };
        contexto.AddRange(contrato, version);
        var claves = new List<string>();
        if (analizado is not null)
        {
            Assert.True(analizado.Valido, string.Join("; ", analizado.Errores));
            var internas = Podadores.Para(tipo)!.Operaciones(spec).Where(o => o.Interna).Select(o => o.Clave).ToHashSet(StringComparer.Ordinal);
            foreach (var o in analizado.Operaciones)
            {
                contexto.Operaciones.Add(new OperacionContrato
                {
                    VersionId = version.Id, Clave = o.Clave, Clase = o.Clase, Metodo = o.Metodo, Ruta = o.Ruta, OperationId = o.OperationId,
                    Resumen = o.Resumen, Etiquetas = [.. o.Etiquetas], Publica = o.Publica,
                });
                if (!internas.Contains(o.Clave))
                {
                    claves.Add(o.Clave);
                }
            }
        }
        else
        {
            claves.Add("rpc:demo.v1.Demo/Get");
        }

        var autor = Guid.NewGuid();
        var guia = new GuiaContrato
        {
            ContratoId = contrato.Id, Titulo = "Primeros pasos", Visibilidad = VisibilidadGuia.Compartible, Estado = EstadoGuia.Publicada,
            RevisionActual = 1, RevisionPublicada = 1, PublicadaPor = autor, PublicadaEn = DateTimeOffset.UtcNow, AprobadaPor = autor,
        };
        var revisionGuia = new RevisionGuia
        {
            GuiaId = guia.Id, Numero = 1, Origen = OrigenRevision.Creacion, Titulo = guia.Titulo,
            Contenido = "# Primeros pasos\n\nAutentícate con `$TOKEN` (pídelo a tu contacto de integraciones).\n",
            Visibilidad = VisibilidadGuia.Compartible, Nota = "Primera versión.", AutorPersonaId = autor, GuardadaEn = DateTimeOffset.UtcNow,
            Huella = new string('b', 64),
        };
        contexto.AddRange(guia, revisionGuia);

        var definicion = new[] { new EntradaPaquete(contrato.Id, version.Id, [.. claves.Order(StringComparer.Ordinal)], [new GuiaEnPaquete(guia.Id, 1)]) };
        var foto = new FotoPaquete("Integración ICT", "Paquete de prueba (Organismo Demo).", definicion);
        var paquete = new PaqueteContratos
        {
            Nombre = foto.Nombre, Descripcion = foto.Descripcion, Estado = EstadoPaquete.Publicado, RevisionActual = 1, RevisionPublicada = 1,
            PublicadaPor = autor, PublicadaEn = DateTimeOffset.UtcNow, CreadoPor = autor,
        };
        contexto.AddRange(paquete, new RevisionPaquete
        {
            PaqueteId = paquete.Id, Numero = 1, Nombre = foto.Nombre, Descripcion = foto.Descripcion, Definicion = foto.DefinicionJson(),
            Nota = "Primera revisión.", AutorPersonaId = autor, GuardadaEn = DateTimeOffset.UtcNow, Huella = foto.Huella(),
        });
        await contexto.SaveChangesAsync(ct);
        return new PaqueteSembrado(contrato.Id, version.Id, paquete.Id, codigo, version.Version, claves.Count);
    }
}

/// <summary>Atajos HTTP de las pruebas de exportaciones.</summary>
internal static class HttpExportaciones
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<(Navegador Navegador, Guid PersonaId)> EmpleadoAsync(
        EntornoApi entorno, WebApplicationFactory<Program> api, string rol, string? baseDeDatos = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = $"2001:db8::45:{Random.Shared.Next(1, 0xffff):x}";
        var grupo = await entorno.GrupoConRolAsync(rol, baseDeDatos);
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"exportaciones.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo Exportaciones", grupo)).StatusCode);
        var yo = await JsonAsync(await navegador.GetAsync("/api/v1/plataforma/yo"));
        return (navegador, Guid.Parse(yo.GetProperty("personaId").GetString()!));
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync(Ct);
        Assert.True(texto.Length > 0, $"{(int)respuesta.StatusCode} sin cuerpo");
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PostAsync(Navegador navegador, string ruta, object cuerpo) =>
        navegador.EnviarAsync(HttpMethod.Post, ruta, JsonContent.Create(cuerpo));

    public static async Task<string?> CodigoAsync(HttpResponseMessage respuesta) =>
        (await JsonAsync(respuesta)).TryGetProperty("codigo", out var c) ? c.GetString() : null;

    /// <summary>Consulta la exportación hasta que sale de pendiente/procesando o pasa el tiempo.</summary>
    public static async Task<JsonElement> EsperarAsync(Navegador navegador, string ruta, TimeSpan maximo)
    {
        var limite = DateTime.UtcNow + maximo;
        while (true)
        {
            var actual = await JsonAsync(await navegador.GetAsync(ruta));
            var estado = actual.GetProperty("estado").GetString();
            if (estado is not ("pendiente" or "procesando") || DateTime.UtcNow > limite)
            {
                return actual;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500), Ct);
        }
    }

    /// <summary>Roles sembrados que exportan internamente (exportaciones.solicitar y contratos.ver_internos).</summary>
    public const string RolExportador = RolesAtlas.Desarrollador;
}
