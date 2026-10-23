using System.Text.Json.Nodes;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Permisos;
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
        ("Identidad", "Ingreso de empleados con Microsoft, proveedores habilitados, cierre de sesión, dispositivos conectados, suspensión y reautenticación (ATLAS-ADR-0004 y ATLAS-ADR-0022)."),
        ("Plataforma", "Datos del usuario en sesión (con sus roles y permisos efectivos) y de la propia API."),
        ("Permisos", "Roles internos y externos, asignaciones con alcance y vigencia, mapeo de grupos de Entra y la matriz rol × permiso generada desde el código (ATLAS-ADR-0023)."),
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
            o.AddOperationTransformer(async (operacion, contexto, _) =>
                DescribirSeguridad(operacion, contexto, await PermisosAsync(contexto).ConfigureAwait(false)));
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

    /// <summary>Permisos del manifiesto que exigen las políticas de la operación (ATLAS-ADR-0023).</summary>
    private static async Task<IReadOnlyList<string>> PermisosAsync(OpenApiOperationTransformerContext contexto)
    {
        var proveedor = contexto.ApplicationServices.GetRequiredService<IAuthorizationPolicyProvider>();
        var permisos = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var politica in contexto.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>()
            .Select(a => a.Policy).Where(p => !string.IsNullOrEmpty(p)))
        {
            var definida = await proveedor.GetPolicyAsync(politica!).ConfigureAwait(false);
            permisos.UnionWith(definida?.Requirements.OfType<RequisitoPermiso>().Select(r => r.Permiso) ?? []);
        }
        return [.. permisos];
    }

    /// <summary>
    /// Seguridad y respuestas comunes de cada operación, deducidas de su metadata: las que piden autorización exigen la
    /// cookie (401 sin sesión), las que cambian estado exigen la cabecera antifalsificación (403 sin ella) y las que
    /// exigen permisos los declaran en <c>x-atlas-permisos</c> (403 sin ellos).
    /// </summary>
    private static void DescribirSeguridad(OpenApiOperation operacion, OpenApiOperationTransformerContext contexto, IReadOnlyList<string> permisos)
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

        var reautenticacion = metadata.OfType<MetadatoReautenticacion>().Any();
        var conPolitica = metadata.OfType<IAuthorizeData>().Any(a => !string.IsNullOrEmpty(a.Policy) || !string.IsNullOrEmpty(a.Roles));

        if (permisos.Count > 0)
        {
            // La PWA y la matriz leen aquí qué permiso pide cada operación.
            operacion.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            operacion.Extensions["x-atlas-permisos"] = new JsonNodeExtension(new JsonArray([.. permisos.Select(p => (JsonNode)p)]));
        }

        operacion.Responses ??= [];
        if (requiereSesion)
        {
            operacion.Responses.TryAdd("401", new OpenApiResponse
            {
                Description = reautenticacion
                    ? $"No hay una sesión activa, o el último ingreso tiene más de 10 minutos (problem+json con codigo «{Reautenticacion.Codigo}»: la PWA lleva a reingresar)."
                    : "No hay una sesión activa o venció.",
            });
        }
        if (cambiaEstado || conPolitica)
        {
            var motivos = new List<string>();
            if (cambiaEstado)
            {
                motivos.Add($"falta la cabecera {EndpointsIdentidad.CabeceraCsrf} o el Origin no es el mismo host");
            }
            if (permisos.Count > 0)
            {
                motivos.Add($"la sesión no tiene el permiso {string.Join(" ni ", permisos)}");
            }
            else if (conPolitica)
            {
                motivos.Add("la sesión no tiene el permiso que exige la operación");
            }
            var texto = string.Join(", o ", motivos);
            operacion.Responses.TryAdd("403", new OpenApiResponse { Description = char.ToUpperInvariant(texto[0]) + texto[1..] + "." });
        }
        if (metadata.OfType<MetadatoLimiteTasa>().Any())
        {
            // Límite de tasa de las rutas de identidad (ATLAS-ADR-0022).
            operacion.Responses.TryAdd("429", new OpenApiResponse
            {
                Description = $"Demasiadas peticiones desde esta IP o este usuario (problem+json con codigo «{LimitacionTasa.Codigo}»).",
                Headers = new Dictionary<string, IOpenApiHeader>
                {
                    ["Retry-After"] = new OpenApiHeader
                    {
                        Description = "Segundos que hay que esperar antes de reintentar.",
                        Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1" },
                    },
                },
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Description = "Problem Details (RFC 9457) con el código que interpreta la PWA.",
                            Properties = new Dictionary<string, IOpenApiSchema>
                            {
                                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 200 },
                                ["codigo"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 50 },
                                ["reintentarEnSegundos"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                            },
                        },
                    },
                },
            });
        }
    }
}
