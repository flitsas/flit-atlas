using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Flit.Atlas.Api;

/// <summary>
/// Contrato OpenAPI 3.1 de atlas-api, generado desde los endpoints (HU 1.7, ATLAS-ADR-0021). La copia versionada es
/// <c>contracts/openapi/atlas.v1.yaml</c>; una prueba falla si el código y el archivo no coinciden.
/// </summary>
internal static class ContratoOpenApi
{
    public const string Documento = "v1";

    /// <summary>Ruta del documento en tiempo de ejecución; solo existe en <see cref="AmbientesConDocumento"/>.</summary>
    public const string RutaYaml = "/openapi/{documentName}.yaml";

    public const string EsquemaSesion = "sesionAtlas";
    public const string EsquemaCsrf = "csrfAtlas";

    /// <summary>
    /// En qa y pdn el documento no se sirve: el contrato vive en el repo y se publica por el módulo de contratos (F3),
    /// con sus permisos. En local (y dev, si se vuelve a desplegar) sirve para explorar la API.
    /// </summary>
    public static readonly string[] AmbientesConDocumento = ["local", "dev"];

    /// <summary>Hosts públicos de ATLAS-ADR-0019. Las rutas del contrato son relativas a ellos.</summary>
    private static readonly (string Url, string Descripcion)[] Servidores =
    [
        ("https://atlas.flitsas.com", "Producción (pdn)"),
        ("https://qa.atlas.flitsas.com", "Pruebas (qa)"),
    ];

    private static readonly (string Nombre, string Descripcion)[] Etiquetas =
    [
        ("Identidad", "Ingreso de empleados con Microsoft, proveedores habilitados y cierre de sesión (ATLAS-ADR-0004)."),
        ("Plataforma", "Datos del usuario en sesión y de la propia API."),
    ];

    public static IServiceCollection AgregarContratoOpenApi(this IServiceCollection servicios) =>
        servicios.AddOpenApi(Documento, o =>
        {
            o.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
            o.AddDocumentTransformer((documento, _, _) =>
            {
                DescribirDocumento(documento);
                return Task.CompletedTask;
            });
            o.AddOperationTransformer((operacion, contexto, _) =>
            {
                DescribirSeguridad(operacion, contexto);
                return Task.CompletedTask;
            });
        });

    public static WebApplication MapContratoOpenApi(this WebApplication app, string ambiente)
    {
        if (AmbientesConDocumento.Contains(ambiente))
        {
            app.MapOpenApi(RutaYaml);
        }
        return app;
    }

    private static void DescribirDocumento(OpenApiDocument documento)
    {
        documento.Info = new OpenApiInfo
        {
            Title = "Flit Atlas",
            Version = "1.0.0",
            Description =
                "API de Flit Atlas para su PWA (BFF, ATLAS-ADR-0008). La sesión viaja en la cookie " +
                $"`{ClaimsAtlas.NombreCookie}` (HttpOnly, Secure, SameSite=Strict); las peticiones que cambian estado " +
                $"exigen además la cabecera `{EndpointsIdentidad.CabeceraCsrf}` y el mismo origen. " +
                "Este archivo se genera desde el código: no se edita a mano.",
        };
        documento.Servers = [.. Servidores.Select(s => new OpenApiServer { Url = s.Url, Description = s.Descripcion })];
        documento.Tags = new HashSet<OpenApiTag>(
            Etiquetas.Select(t => new OpenApiTag { Name = t.Nombre, Description = t.Descripcion }));

        documento.Components ??= new OpenApiComponents();
        documento.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            [EsquemaSesion] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = ClaimsAtlas.NombreCookie,
                Description = "Cookie de sesión del BFF. La emite el ingreso y solo lleva el id cifrado de la sesión, que vive en el servidor.",
            },
            [EsquemaCsrf] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = EndpointsIdentidad.CabeceraCsrf,
                Description = "Antifalsificación: obligatoria (cualquier valor) en POST, PUT, PATCH y DELETE bajo /api/. Sin ella, o con otro Origin, la respuesta es 403.",
            },
        };
    }

    /// <summary>
    /// Seguridad y respuestas comunes de cada operación, deducidas de su metadata: las que piden autorización exigen la
    /// cookie (401 sin sesión) y las que cambian estado exigen la cabecera antifalsificación (403 sin ella).
    /// </summary>
    private static void DescribirSeguridad(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto)
    {
        var metadata = contexto.Description.ActionDescriptor.EndpointMetadata;
        var requiereSesion = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
        var metodo = contexto.Description.HttpMethod ?? HttpMethods.Get;
        var cambiaEstado = !(HttpMethods.IsGet(metodo) || HttpMethods.IsHead(metodo) || HttpMethods.IsOptions(metodo));

        var requisito = new OpenApiSecurityRequirement();
        if (requiereSesion)
        {
            requisito[new OpenApiSecuritySchemeReference(EsquemaSesion, contexto.Document)] = [];
        }
        if (cambiaEstado)
        {
            requisito[new OpenApiSecuritySchemeReference(EsquemaCsrf, contexto.Document)] = [];
        }
        if (requisito.Count > 0)
        {
            operacion.Security = [requisito];
        }

        operacion.Responses ??= [];
        if (requiereSesion)
        {
            operacion.Responses.TryAdd("401", new OpenApiResponse { Description = "No hay una sesión activa o venció." });
        }
        if (cambiaEstado)
        {
            operacion.Responses.TryAdd("403", new OpenApiResponse
            {
                Description = $"Falta la cabecera {EndpointsIdentidad.CabeceraCsrf} o el Origin no es el mismo host.",
            });
        }
    }
}
