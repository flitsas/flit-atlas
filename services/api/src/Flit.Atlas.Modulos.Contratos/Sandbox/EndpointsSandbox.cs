using System.ComponentModel;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Contratos.Sandbox;

/// <summary>
/// Sandbox nivel 1 (HU 4.7, plan 00 §8.5 y §8.8, ATLAS-ADR-0045):
/// <list type="bullet">
/// <item>Portal (<c>/api/v1/portal/sandbox</c>): activar el sandbox de un paquete (la llave se muestra una sola vez) y listar
/// los de la organización con su URL base, sus instrucciones y un ejemplo con curl. Exige <c>AccesoPortal.PuedeUsarSandboxAsync</c>.</item>
/// <item>Interno: los sandboxes de una organización (<c>atlas.paquetes.gestionar</c>).</item>
/// <item>Proxy (<c>/api/v1/contratos/sandbox/{ruta}/{contrato}/…</c>): sin sesión; se autentica con la llave del sandbox en
/// <c>Authorization: Bearer</c>, revisa en cada llamada que la compartición siga permitiéndolo y reenvía a Microcks, que
/// nunca se expone. Las operaciones no compartidas no existen en el mock (se importa el spec podado): 404.</item>
/// </list>
/// </summary>
internal static class EndpointsSandbox
{
    public const string RutaProxy = "/sandbox/";
    public const string Cabecera = "Authorization";
    public const string VariableLlave = "LLAVE_SANDBOX";

    private static readonly string[] Metodos = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    /// <summary>Cabeceras de la petición que no pasan a Microcks: la llave, la sesión, el salto y lo que dice quién llama.</summary>
    private static readonly HashSet<string> CabecerasQueNoPasan = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Host", "Connection", "Keep-Alive", "Proxy-Authorization", "Proxy-Connection", "TE", "Trailer",
        "Transfer-Encoding", "Upgrade", "Forwarded", "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "X-Real-IP", "Origin",
        "Referer", "Content-Length", "Content-Type", "Accept-Encoding", "Expect",
    };

    /// <summary>
    /// Cabeceras de la respuesta de Microcks que llegan al cliente (lista blanca, F4-SEC-26): las de contenido y las propias
    /// del contrato (<c>X-…</c>, salvo las de <see cref="PrefijosQueNoPasan"/>). Nada de <c>Location</c>, cookies, CORS, CSP
    /// ni <c>Server</c>: el mock no decide la seguridad de la respuesta de Atlas.
    /// </summary>
    private static readonly HashSet<string> RespuestaQuePasa = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Language", "Content-Encoding", "ETag", "Last-Modified", "Retry-After", "Vary",
    };

    /// <summary>Cabeceras <c>X-…</c> del mock que no pasan: las de Atlas, las de seguridad (las pone Atlas) y las del servidor.</summary>
    private static readonly string[] PrefijosQueNoPasan =
    [
        "X-Atlas-", "X-Microcks-", "X-Forwarded-", "X-Powered-By", "X-AspNet", "X-Frame-Options", "X-Content-Type-Options",
        "X-XSS-Protection", "X-Robots-Tag", "X-Permitted-Cross-Domain-Policies",
    ];

    /// <summary>Acción de la auditoría cuando el proxy niega una llamada a un sandbox que existe (F4-SEC-25).</summary>
    public const string AccionNegado = "contratos.sandbox_acceso_negado";

    /// <summary>
    /// Cuánto se guarda la revisión de que quien activó el sandbox sigue teniendo acceso (F4-SEC-22): la llave es de esa
    /// persona y deja de servir, a lo sumo en este tiempo, cuando pierde el rol, el permiso o el alcance.
    /// </summary>
    internal static readonly TimeSpan VidaRevisionActivador = TimeSpan.FromSeconds(30);

    internal static bool PasaAlCliente(string nombre) =>
        RespuestaQuePasa.Contains(nombre)
        || (nombre.StartsWith("X-", StringComparison.OrdinalIgnoreCase) && !PrefijosQueNoPasan.Any(p => nombre.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    // --- DTO ---

    [Description("Activar (o regenerar la llave de) el sandbox de un paquete compartido.")]
    public sealed record SolicitudSandbox([property: Description("Id del paquete compartido.")] Guid PaqueteId);

    [Description("Un contrato del sandbox con la URL base de su mock.")]
    public sealed record ContratoSandbox(
        Guid ContratoId,
        string Codigo,
        string Nombre,
        [property: Description("Versión semántica del contrato importada.")] string Version,
        [property: Description("URL base del mock: se le agrega la ruta de la operación (p. ej. /tramites/{id}).")] string UrlBase,
        [property: Description("Operaciones compartidas que responde el mock; las demás responden 404.")] int Operaciones);

    [Description("Un sandbox nivel 1 de la organización (sin la llave: solo se muestra al activarlo).")]
    public sealed record SandboxPortal(
        Guid Id,
        Guid PaqueteId,
        string Paquete,
        [property: Description("Revisión del paquete importada en el mock.")] int Revision,
        [property: Description("1 = Microcks (respuestas de ejemplo del spec).")] int Nivel,
        [property: Description("activo o apagado.")] string Estado,
        [property: Description("Si está apagado: revocada, vencida, sin_sandbox, paquete_no_disponible o sin_contratos.")] string? MotivoApagado,
        DateTimeOffset ActivadoEn,
        [property: Description("Cuándo se generó la llave vigente.")] DateTimeOffset LlaveCreadaEn,
        [property: Description("Vence con la compartición; vacío = sin vencimiento.")] DateTimeOffset? ExpiraEn,
        DateTimeOffset? UltimoUsoEn,
        [property: Description("Cabecera con la que se manda la llave.")] string Cabecera,
        IReadOnlyList<ContratoSandbox> Contratos,
        [property: Description("Cómo usarlo, en texto.")] string Instrucciones,
        [property: Description("Ejemplo con curl; la llave va en la variable $LLAVE_SANDBOX, nunca en claro.")] string EjemploCurl);

    [Description("Los sandboxes de la organización y si el sandbox está disponible en este ambiente.")]
    public sealed record SandboxesPortal(
        [property: Description("Falso si este ambiente no tiene Microcks configurado: el botón Sandbox sale deshabilitado.")] bool Disponible,
        string? Motivo,
        IReadOnlyList<SandboxPortal> Sandboxes);

    [Description("Sandbox activado: la llave solo se muestra esta vez (Atlas guarda su hash).")]
    public sealed record SandboxActivado(
        SandboxPortal Sandbox,
        [property: Description("Llave del sandbox, en claro una sola vez. Mándala como Authorization: Bearer <llave>.")] string Llave);

    [Description("Un sandbox de una organización, para la administración.")]
    public sealed record SandboxDeOrganizacion(
        Guid Id, Guid PaqueteId, string? Paquete, int Revision, int Nivel, string Estado, string? MotivoApagado, Guid ActivadoPor,
        DateTimeOffset ActivadoEn, DateTimeOffset? ExpiraEn, DateTimeOffset? UltimoUsoEn,
        [property: Description("Contratos importados en Microcks.")] int Contratos,
        [property: Description("Servicios que faltan por borrar de Microcks (se reintenta solo).")] int PorBorrar);

    [Description("Por qué no se pudo, con un código estable.")]
    public sealed record ErrorSandbox(
        [property: Description("no_encontrado (404); solo_resumen, no_permitido_por_comparticion o sin_permiso (403); sin_contratos_mock (409); microcks_fallo (502); sandbox_no_disponible (503); sin_llave (401).")] string Codigo,
        string Mensaje);

    public static void MapearPortal(RouteGroupBuilder portal)
    {
        portal.MapGet("/sandbox", ListarAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ListarSandboxesPortal")
            .WithSummary("Mis sandboxes")
            .WithDescription("Los sandboxes de la organización de los paquetes en los que la persona puede usar el sandbox, con la URL base de cada contrato, las instrucciones y un ejemplo con curl (la llave nunca: solo se muestra al activarlo). disponible = falso si el ambiente no tiene Microcks. Requiere atlas.portal.ver.")
            .Produces<SandboxesPortal>();

        portal.MapPost("/sandbox", ActivarAsync)
            .RequierePermiso(PermisosContratos.PortalVer)
            .WithName("ActivarSandboxPortal")
            .WithSummary("Activar el sandbox de un paquete")
            .WithDescription("Importa a Microcks el spec podado de cada contrato OpenAPI del paquete (solo las operaciones compartidas; las demás responden 404) y entrega una llave nueva, que se muestra una sola vez. Si ya estaba activo, regenera la llave (la anterior deja de servir) y reimporta si cambió lo compartido: 200. Exige que la compartición permita el sandbox y que la persona tenga atlas.sandbox.usar o el permiso extra sandbox de su invitación. 404 si el paquete no está compartido con la organización; 503 si el ambiente no tiene Microcks. Auditado. Requiere atlas.portal.ver.")
            .Produces<SandboxActivado>(StatusCodes.Status201Created)
            .Produces<SandboxActivado>()
            .Produces<ErrorSandbox>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSandbox>(StatusCodes.Status404NotFound)
            .Produces<ErrorSandbox>(StatusCodes.Status409Conflict)
            .Produces<ErrorSandbox>(StatusCodes.Status502BadGateway)
            .Produces<ErrorSandbox>(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();
    }

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/organizaciones/{organizacionId:guid}/sandboxes", DeLaOrganizacionAsync)
            .RequierePermiso(PermisosContratos.PaquetesGestionar)
            .WithName("ListarSandboxesOrganizacion")
            .WithSummary("Sandboxes de una organización")
            .WithDescription("Para la administración: los sandboxes de la organización (activos y apagados) con su paquete, revisión, vigencia, último uso y borrados pendientes en Microcks. Nunca la llave ni su hash. Requiere atlas.paquetes.gestionar.")
            .Produces<List<SandboxDeOrganizacion>>();

        // El proxy: sin sesión (lo llaman los sistemas del cliente con curl o su código). Se autentica con la llave; no está
        // en el contrato OpenAPI de Atlas porque reenvía cualquier ruta del contrato del cliente.
        // Busca la llave en los sandboxes de todas las organizaciones: alcance del sistema, pedido aquí (ATLAS-ADR-0050).
        grupo.MapMethods(RutaProxy + "{ruta}/{contrato}/{**resto}", Metodos, ProxyAsync)
            .AllowAnonymous()
            .ConAlcanceDelSistemaSinSesion()
            .ExcludeFromDescription()
            .WithName("ProxySandbox");
    }

    // --- Portal ---

    private static async Task<IResult> ListarAsync(
        HttpContext http, AccesoPortal acceso, ContratosDbContext contexto, IOptions<OpcionesSandbox> opciones, IConfiguration configuracion,
        CancellationToken ct)
    {
        var vista = await acceso.VistaAsync(http.User, ct);
        var conSandbox = vista.Paquetes.Where(p => p.PuedeSandbox).ToDictionary(p => p.Paquete.Id);
        var ids = conSandbox.Keys.ToList();
        // Siempre la organización de la sesión (además del predicado y RLS).
        var sandboxes = await contexto.Sandboxes.AsNoTracking()
            .Where(s => s.OrganizacionId == vista.OrganizacionId && ids.Contains(s.PaqueteId))
            .ToListAsync(ct);
        var raiz = Raiz(http, opciones.Value, configuracion);
        var disponible = opciones.Value.Disponible;
        return Results.Ok(new SandboxesPortal(disponible, disponible ? null : "El sandbox no está disponible en este ambiente.",
            [.. sandboxes.OrderBy(s => conSandbox[s.PaqueteId].Paquete.Nombre, StringComparer.CurrentCultureIgnoreCase).Select(s => Dto(s, conSandbox[s.PaqueteId], raiz))]));
    }

    private static async Task<IResult> ActivarAsync(
        [Description("El paquete.")] SolicitudSandbox? solicitud,
        HttpContext http, AccesoPortal acceso, ServicioSandbox servicio, PlataformaDbContext plataforma, IOptions<OpcionesSandbox> opciones,
        IConfiguration configuracion, TimeProvider reloj, CancellationToken ct)
    {
        if (solicitud is null || solicitud.PaqueteId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["paqueteId"] = ["Indica el paquete."] });
        }
        var decision = await acceso.PuedeUsarSandboxAsync(http.User, solicitud.PaqueteId, ct);
        if (!decision.Permitida)
        {
            return decision.Codigo switch
            {
                "solo_resumen" => EndpointsPortal.SoloResumen(),
                "no_permitido_por_comparticion" => Error(StatusCodes.Status403Forbidden, decision.Codigo,
                    "La compartición de este paquete con tu organización no incluye el sandbox. Pídeselo a tu contacto en FLIT."),
                "sin_permiso" => Error(StatusCodes.Status403Forbidden, decision.Codigo,
                    "Tu rol no incluye el sandbox de este paquete (atlas.sandbox.usar o el permiso sandbox de tu invitación)."),
                _ => await EndpointsPortal.NegadoAsync(http, plataforma, reloj, await acceso.VistaAsync(http.User, ct), solicitud.PaqueteId, "sandbox", ct),
            };
        }
        if (!servicio.Disponible)
        {
            return NoDisponible();
        }
        var persona = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona), out var p) ? p : Guid.Empty;
        var resultado = await servicio.ActivarAsync(decision.Paquete!, persona, http.Connection.RemoteIpAddress, http.TraceIdentifier, ct);
        if (resultado.Sandbox is not { } sandbox)
        {
            return resultado.Codigo == "sin_contratos_mock"
                ? Error(StatusCodes.Status409Conflict, resultado.Codigo, resultado.Mensaje!)
                : Error(StatusCodes.Status502BadGateway, resultado.Codigo!, "Microcks no pudo importar el paquete. Inténtalo de nuevo en unos minutos.");
        }
        var cuerpo = new SandboxActivado(Dto(sandbox, decision.Paquete!, Raiz(http, opciones.Value, configuracion)), resultado.Llave!);
        http.Response.Headers.CacheControl = "no-store";
        return resultado.Creado ? Results.Json(cuerpo, statusCode: StatusCodes.Status201Created) : Results.Ok(cuerpo);
    }

    // --- Interno ---

    private static async Task<IResult> DeLaOrganizacionAsync(
        [Description("Id de la organización.")] Guid organizacionId, ContratosDbContext contexto, CancellationToken ct)
    {
        var sandboxes = await contexto.Sandboxes.AsNoTracking().Where(s => s.OrganizacionId == organizacionId).ToListAsync(ct);
        var ids = sandboxes.Select(s => s.PaqueteId).Distinct().ToList();
        var nombres = await contexto.Paquetes.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Nombre, ct);
        return Results.Ok(sandboxes
            .OrderBy(s => nombres.GetValueOrDefault(s.PaqueteId) ?? "~", StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.Id)
            .Select(s => new SandboxDeOrganizacion(s.Id, s.PaqueteId, nombres.GetValueOrDefault(s.PaqueteId), s.Revision, s.Nivel, Texto(s.Estado),
                s.MotivoApagado, s.ActivadoPor, s.ActivadoEn, s.ExpiraEn, s.UltimoUsoEn, ServicioSandbox.Leer(s.Contratos).Count,
                ServicioSandbox.LeerPorBorrar(s.PorBorrar).Count))
            .ToList());
    }

    // --- Proxy ---

    private static async Task<IResult> ProxyAsync(
        string ruta, string contrato, string? resto, HttpContext http, ContratosDbContext contexto, PlataformaDbContext plataforma,
        IClienteMicrocks microcks, AccesoPortal acceso, CalculadoraPermisos calculadora, IMemoryCache cache,
        IOptions<OpcionesSandbox> opciones, TimeProvider reloj, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        if (!microcks.Disponible)
        {
            return NoDisponible();
        }
        var autorizacion = http.Request.Headers.Authorization.ToString();
        if (!autorizacion.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || autorizacion.Length <= 7)
        {
            http.Response.Headers.WWWAuthenticate = "Bearer realm=\"atlas-sandbox\"";
            return Error(StatusCodes.Status401Unauthorized, "sin_llave", $"Manda la llave del sandbox en la cabecera Authorization: Bearer ${VariableLlave}.");
        }
        var llave = autorizacion[7..].Trim();
        if (ruta.Length != 32 || !ruta.All(char.IsAsciiHexDigitLower))
        {
            return NoEsta();
        }
        var sandbox = await contexto.Sandboxes.AsNoTracking().FirstOrDefaultAsync(s => s.Ruta == ruta, ct);
        // La misma respuesta si no existe o si la llave no es la suya: no se revela si la ruta existe.
        if (sandbox is null)
        {
            return NoEsta();
        }
        if (!CryptographicOperations.FixedTimeEquals(sandbox.LlaveHash, ServicioSandbox.Hash(llave)))
        {
            return await NegadoAsync(http, plataforma, reloj, sandbox, "llave_invalida", ct);
        }
        var ahora = reloj.GetUtcNow();
        // En cada llamada, aunque el trabajo periódico aún no lo haya apagado: compartición vigente, con sandbox y paquete publicado.
        var comparticion = await contexto.Comparticiones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == sandbox.ComparticionId && c.OrganizacionId == sandbox.OrganizacionId, ct);
        var paquete = await contexto.Paquetes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sandbox.PaqueteId, ct);
        if (sandbox.Estado != EstadoSandbox.Activo || sandbox.ExpiraEn <= ahora || ServicioSandbox.MotivoParaApagar(comparticion, paquete, ahora) is not null)
        {
            return await NegadoAsync(http, plataforma, reloj, sandbox, "sandbox_inactivo", ct);
        }
        // La llave es de quien activó el sandbox: si perdió el acceso al paquete (rol, permiso, alcance, suspensión), deja
        // de servir aunque la compartición siga (F4-SEC-22).
        if (!await ActivadorConservaAccesoAsync(sandbox, acceso, calculadora, cache, ct))
        {
            return await NegadoAsync(http, plataforma, reloj, sandbox, "activador_sin_acceso", ct);
        }
        if (ServicioSandbox.Leer(sandbox.Contratos).FirstOrDefault(c => c.Segmento == contrato) is not { } importado)
        {
            return NoEsta();
        }
        if (RutaSegura(resto) is not { } segura)
        {
            return Error(StatusCodes.Status400BadRequest, "ruta_invalida", "La ruta no puede tener segmentos «.» ni «..».");
        }

        byte[]? cuerpo = null;
        var maximo = Math.Max(1, opciones.Value.CuerpoMaximoKiB) * 1024;
        if (http.Request.ContentLength > maximo)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "cuerpo_muy_grande", $"El sandbox acepta cuerpos de hasta {opciones.Value.CuerpoMaximoKiB} KiB.");
        }
        if (http.Request.ContentLength > 0 || http.Request.Headers.TransferEncoding.Count > 0)
        {
            using var memoria = new MemoryStream();
            var bufer = new byte[16 * 1024];
            int leidos;
            while ((leidos = await http.Request.Body.ReadAsync(bufer, ct)) > 0)
            {
                if (memoria.Length + leidos > maximo)
                {
                    return Error(StatusCodes.Status413PayloadTooLarge, "cuerpo_muy_grande", $"El sandbox acepta cuerpos de hasta {opciones.Value.CuerpoMaximoKiB} KiB.");
                }
                memoria.Write(bufer, 0, leidos);
            }
            cuerpo = memoria.ToArray();
        }
        var cabeceras = http.Request.Headers
            .Where(h => !CabecerasQueNoPasan.Contains(h.Key) && !h.Key.StartsWith("X-Atlas-", StringComparison.OrdinalIgnoreCase))
            .Select(h => new KeyValuePair<string, string[]>(h.Key, h.Value.Where(v => v is not null).Select(v => v!).ToArray()))
            .ToList();

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await microcks.LlamarMockAsync(importado.Servicio, importado.Version, segura, http.Request.QueryString.Value,
                new HttpMethod(http.Request.Method), cabeceras, cuerpo, http.Request.ContentType, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or ErrorMicrocks || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return Error(StatusCodes.Status502BadGateway, "microcks_fallo", "El sandbox no respondió. Inténtalo de nuevo en unos minutos.");
        }

        using (respuesta)
        {
            // Último uso, a lo sumo una escritura por hora.
            var hace = ahora.AddHours(-1);
            await contexto.Sandboxes.Where(s => s.Id == sandbox.Id && (s.UltimoUsoEn == null || s.UltimoUsoEn < hace))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoUsoEn, ahora), ct);

            // Una redirección del mock no se sigue (AllowAutoRedirect=false) ni se reenvía su Location (F4-SEC-26).
            http.Response.StatusCode = (int)respuesta.StatusCode;
            foreach (var (nombre, valores) in respuesta.Headers.Concat(respuesta.Content.Headers))
            {
                if (PasaAlCliente(nombre))
                {
                    http.Response.Headers[nombre] = valores.ToArray();
                }
            }
            http.Response.Headers["X-Atlas-Sandbox"] = "nivel-1";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; sandbox";
            if (!HttpMethods.IsHead(http.Request.Method))
            {
                await respuesta.Content.CopyToAsync(http.Response.Body, ct);
            }
        }
        return Results.Empty;
    }

    /// <summary>
    /// La ruta de la operación, escapada segmento por segmento; <c>null</c> si trae <c>.</c> o <c>..</c> (no se puede salir del
    /// servicio del sandbox hacia otro servicio de Microcks).
    /// </summary>
    internal static string? RutaSegura(string? resto)
    {
        if (string.IsNullOrEmpty(resto))
        {
            return string.Empty;
        }
        var segmentos = resto.Split('/');
        if (segmentos.Any(s => s is "." or ".." || s.Contains('\\', StringComparison.Ordinal)))
        {
            return null;
        }
        return string.Join('/', segmentos.Select(Uri.EscapeDataString));
    }

    // --- Piezas comunes ---

    /// <summary>Raíz pública del sandbox: <c>Contratos:Sandbox:UrlPublica</c>, si no <c>Atlas:UrlPublica</c>, si no la de la petición.</summary>
    internal static string Raiz(HttpContext http, OpcionesSandbox opciones, IConfiguration configuracion)
    {
        var raiz = opciones.UrlPublica?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(raiz))
        {
            raiz = configuracion["Atlas:UrlPublica"]?.Trim().TrimEnd('/');
        }
        if (string.IsNullOrEmpty(raiz))
        {
            raiz = $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}";
        }
        return $"{raiz}/api/v1/{ModuloContratos.IdModulo}{RutaProxy}";
    }

    private static SandboxPortal Dto(SandboxPaquete s, PaqueteDelPortal paquete, string raiz)
    {
        // Solo los contratos que la persona ve con nivel técnico (el sandbox es de la organización).
        var visibles = paquete.Contratos.Where(c => c.Tecnico).ToDictionary(c => c.Contrato.Id);
        var contratos = ServicioSandbox.Leer(s.Contratos)
            .Where(c => visibles.ContainsKey(c.ContratoId))
            .Select(c => (Importado: c, Visible: visibles[c.ContratoId]))
            .ToList();
        var lista = contratos.Select(x => new ContratoSandbox(x.Importado.ContratoId, x.Visible.Contrato.Codigo, x.Visible.Contrato.Nombre,
            x.Visible.Version.Version, $"{raiz}{s.Ruta}/{x.Importado.Segmento}", x.Importado.Operaciones)).ToList();
        var ejemplo = contratos
            .SelectMany(x => x.Visible.Entrada.Operaciones.Select(o => (Base: $"{raiz}{s.Ruta}/{x.Importado.Segmento}", Clave: o)))
            .Select(x => (x.Base, Partes: x.Clave.Split(' ', 2)))
            .FirstOrDefault(x => x.Partes.Length == 2 && x.Partes[1].StartsWith('/'));
        var curl = ejemplo.Partes is null
            ? $"curl -g -H \"Authorization: Bearer ${VariableLlave}\" \"{lista.FirstOrDefault()?.UrlBase ?? raiz}/<ruta>\""
            : $"curl -g -X {ejemplo.Partes[0]} -H \"Authorization: Bearer ${VariableLlave}\" \"{ejemplo.Base}{ejemplo.Partes[1]}\"";
        var instrucciones =
            $"1. Guarda la llave en la variable de entorno {VariableLlave} (export {VariableLlave}='<tu llave>'); no la escribas en el código ni la compartas. " +
            "2. Llama a la URL base de cada contrato más la ruta de la operación, con la cabecera Authorization: Bearer y la llave. " +
            "3. Responde con los ejemplos del spec (nivel 1, Microcks): reemplaza los parámetros de la ruta por los valores de los ejemplos. " +
            "Las operaciones que no se te comparten responden 404. Si pierdes la llave, genera una nueva: la anterior deja de servir.";
        return new SandboxPortal(s.Id, s.PaqueteId, paquete.RevisionPaquete.Nombre, s.Revision, s.Nivel, Texto(s.Estado), s.MotivoApagado, s.ActivadoEn,
            s.LlaveCreadaEn, s.ExpiraEn, s.UltimoUsoEn, $"{Cabecera}: Bearer ${VariableLlave}", lista, instrucciones, curl);
    }

    private static string Texto(EstadoSandbox estado) => estado == EstadoSandbox.Activo ? "activo" : "apagado";

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorSandbox(codigo, mensaje), statusCode: estado);

    /// <summary>
    /// ¿Quien activó el sandbox sigue pudiendo usarlo? Las mismas reglas del portal (<see cref="AccesoPortal.VistaDeAsync"/>)
    /// con sus permisos de ahora, guardado <see cref="VidaRevisionActivador"/> para no recalcular en cada llamada.
    /// </summary>
    private static async Task<bool> ActivadorConservaAccesoAsync(
        SandboxPaquete sandbox, AccesoPortal acceso, CalculadoraPermisos calculadora, IMemoryCache cache, CancellationToken ct)
    {
        var clave = $"atlas.contratos.sandbox.activador:{sandbox.Id}:{sandbox.ActivadoPor}:{sandbox.Revision}";
        if (cache.TryGetValue(clave, out bool vale))
        {
            return vale;
        }
        var permisos = await calculadora.CalcularAsync(sandbox.ActivadoPor, ct);
        var vista = await acceso.VistaDeAsync(sandbox.ActivadoPor, sandbox.OrganizacionId, permisos, ct);
        var paquete = vista.Paquetes.FirstOrDefault(p => p.Paquete.Id == sandbox.PaqueteId);
        vale = paquete is { Nivel: NivelPortal.Tecnico, PuedeSandbox: true } && paquete.Comparticion.PuedeSandbox;
        cache.Set(clave, vale, VidaRevisionActivador);
        return vale;
    }

    /// <summary>404 como si no existiera, auditado con la organización del sandbox (F4-SEC-25); nunca la llave.</summary>
    private static async Task<IResult> NegadoAsync(
        HttpContext http, PlataformaDbContext plataforma, TimeProvider reloj, SandboxPaquete sandbox, string motivo, CancellationToken ct)
    {
        plataforma.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = reloj.GetUtcNow(),
            ActorTipo = TipoActor.Sistema,
            ActorId = "sandbox",
            OrganizacionId = sandbox.OrganizacionId,
            Accion = AccionNegado,
            RecursoTipo = "contratos_sandbox",
            RecursoId = sandbox.Id.ToString(),
            Ip = http.Connection.RemoteIpAddress,
            Datos = JsonSerializer.Serialize(new { motivo, paqueteId = sandbox.PaqueteId }, JsonSerializerOptions.Web),
            CorrelacionId = http.TraceIdentifier,
        });
        await plataforma.SaveChangesAsync(ct);
        return NoEsta();
    }

    private static IResult NoEsta() => Error(StatusCodes.Status404NotFound, "no_encontrado", "El sandbox no existe, la llave no es la suya o ya no está activo.");

    private static IResult NoDisponible() =>
        Error(StatusCodes.Status503ServiceUnavailable, "sandbox_no_disponible", "El sandbox no está disponible en este ambiente.");
}
