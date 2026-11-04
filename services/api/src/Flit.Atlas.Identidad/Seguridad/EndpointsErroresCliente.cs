using System.ComponentModel;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Flit.Atlas.Identidad.Sesiones;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Flit.Atlas.Identidad.Seguridad;

/// <summary>
/// Reporte de los errores de la PWA que llegan a la pantalla «Algo salió mal» (<c>POST /api/v1/plataforma/errores-cliente</c>).
/// Se escriben como advertencia en el log estructurado (Loki), con la referencia corta que la persona ve en pantalla; no
/// van a la base ni a la auditoría. Exige la sesión del BFF (empleado, externo o la cuenta de emergencia; un token Bearer
/// de OpenIddict no sirve): así nadie de fuera llena el log. Los errores antes de tener sesión (invitación, ingreso) no
/// se reportan; la PWA igual muestra la referencia. Tiene límite de tasa por persona
/// (<see cref="OpcionesLimiteTasa.ErroresClientePorPersona"/>) y por IP (<see cref="OpcionesLimiteTasa.ErroresClientePorIp"/>)
/// y un tope de tamaño del cuerpo. Vive en la identidad porque usa su sesión y su limitación de tasa.
/// <para>
/// Sin datos personales (prompt maestro §13): solo viaja lo técnico, la ruta se guarda sin consulta ni fragmento, y el
/// servidor tacha además correos, números largos (cédulas, teléfonos) y cadenas con forma de token antes de registrar.
/// De la persona solo queda su id (nunca su correo ni su nombre), para cruzar el error con su sesión.
/// </para>
/// </summary>
public static partial class EndpointsErroresCliente
{
    public const string Ruta = "/api/v1/plataforma/errores-cliente";

    /// <summary>Tope del cuerpo en bytes: un reporte normal pesa menos de 4 KB.</summary>
    public const int TamanoMaximo = 8 * 1024;

    public const int LargoMensaje = 500;
    public const int LargoPila = 4000;
    public const int LargoNombre = 100;
    public const int LargoRuta = 200;
    public const int LargoVersion = 50;

    private const string AlfabetoReferencia = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Tachado = "[tachado]";

    [Description("Error de la PWA que la persona vio en la pantalla «Algo salió mal». Solo datos técnicos.")]
    public sealed record ReporteErrorCliente(
        [property: Description("Referencia corta que la PWA mostró a la persona (6 a 12 letras mayúsculas o dígitos). Si no es válida, el servidor asigna otra.")] string? Referencia,
        [property: Description("Tipo del error (TypeError, Error…).")] string? Nombre,
        [property: Description("Mensaje del error (se recorta a 500 caracteres).")] string? Mensaje,
        [property: Description("Pila del error (se recorta a 4000 caracteres).")] string? Pila,
        [property: Description("Ruta de la PWA donde ocurrió, sin consulta ni fragmento.")] string? Ruta,
        [property: Description("Versión de la PWA.")] string? Version);

    [Description("Reporte recibido: la referencia con la que quedó en el log.")]
    public sealed record ErrorClienteRegistrado(string Referencia);

    /// <summary>Tope de tamaño del cuerpo que Kestrel aplica al endpoint (además de la lectura acotada del manejador).</summary>
    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => TamanoMaximo;
    }

    internal static void MapErroresCliente(this IEndpointRouteBuilder rutas)
    {
        rutas.MapPost(Ruta, RegistrarAsync)
            .RequireAuthorization()
            .WithMetadata(new MetadatoLimiteTasa(), new LimiteCuerpo())
            .Accepts<ReporteErrorCliente>("application/json")
            .WithName("ReportarErrorDeCliente")
            .WithTags("Plataforma")
            .WithSummary("Reportar un error de la PWA")
            .WithDescription(
                "La PWA envía aquí los errores de la pantalla «Algo salió mal» (salvo los de descarga de un módulo tras un " +
                "despliegue, que se resuelven recargando). Se escriben como advertencia en el log estructurado con la " +
                "referencia que la persona ve y el id de la persona (no su correo); no se guardan en la base. Requiere la " +
                "sesión de la PWA (empleado, externo o cuenta de emergencia; no acepta tokens Bearer): sin sesión responde " +
                "401 y la PWA no reporta. Tiene límite de tasa por persona y por IP y un cuerpo de máximo " +
                $"{TamanoMaximo / 1024} KB. El servidor recorta los campos y tacha correos, números largos y cadenas con " +
                "forma de token.")
            .Produces<ErrorClienteRegistrado>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status413PayloadTooLarge);
    }

    private static async Task<IResult> RegistrarAsync(HttpContext http, ClaimsPrincipal usuario, ILoggerFactory logs, CancellationToken ct)
    {
        if (http.Request.ContentLength > TamanoMaximo)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Lectura acotada: aunque no venga Content-Length (chunked), nunca se lee más del tope.
        var bytes = new byte[TamanoMaximo + 1];
        var leidos = 0;
        int n;
        while (leidos < bytes.Length && (n = await http.Request.Body.ReadAsync(bytes.AsMemory(leidos), ct)) > 0)
        {
            leidos += n;
        }
        if (leidos > TamanoMaximo)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        ReporteErrorCliente? reporte;
        try
        {
            reporte = JsonSerializer.Deserialize<ReporteErrorCliente>(bytes.AsSpan(0, leidos), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            reporte = null;
        }
        if (reporte is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reporte"] = ["El cuerpo debe ser un reporte de error en JSON."] });
        }

        var referencia = reporte.Referencia is { } r && ReferenciaValida().IsMatch(r)
            ? r
            : RandomNumberGenerator.GetString(AlfabetoReferencia, 8);
        var version = reporte.Version is { } v && VersionValida().IsMatch(v) ? v : null;

        ErrorRegistrado(
            logs.CreateLogger("Flit.Atlas.ErroresCliente"),
            referencia,
            Limpiar(reporte.Nombre, LargoNombre) ?? "desconocido",
            Limpiar(reporte.Mensaje, LargoMensaje) ?? string.Empty,
            LimpiarRuta(reporte.Ruta),
            version,
            Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var persona) ? persona : null,
            Limpiar(reporte.Pila, LargoPila, multilinea: true));

        return Results.Accepted(value: new ErrorClienteRegistrado(referencia));
    }

    /// <summary>Recorta, quita caracteres de control (salvo saltos de línea en la pila) y tacha lo que parezca personal.</summary>
    public static string? Limpiar(string? texto, int maximo, bool multilinea = false)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }
        var recortado = texto.Length > maximo ? texto[..maximo] : texto;
        var limpio = new StringBuilder(recortado.Length);
        foreach (var c in recortado)
        {
            if (!char.IsControl(c) || (multilinea && c == '\n'))
            {
                limpio.Append(c);
            }
            else if (c is '\n' or '\r' or '\t')
            {
                limpio.Append(' ');
            }
        }
        var resultado = Correo().Replace(limpio.ToString(), Tachado);
        resultado = Token().Replace(resultado, Tachado);
        resultado = NumeroLargo().Replace(resultado, Tachado);
        return resultado.Trim();
    }

    /// <summary>Solo la ruta: sin esquema, host, consulta ni fragmento (el fragmento puede traer el token de una invitación).</summary>
    public static string? LimpiarRuta(string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || !ruta.StartsWith('/') || ruta.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }
        var fin = ruta.IndexOfAny(['?', '#']);
        return Limpiar(fin >= 0 ? ruta[..fin] : ruta, LargoRuta);
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Warning,
        Message = "Error en la PWA {Referencia}: {Nombre}: {Mensaje} (ruta {Ruta}, versión {Version}, persona {PersonaId}) {Pila}")]
    private static partial void ErrorRegistrado(
        ILogger log, string referencia, string nombre, string mensaje, string? ruta, string? version, Guid? personaId, string? pila);

    [GeneratedRegex("^[A-Z0-9]{6,12}$")]
    private static partial Regex ReferenciaValida();

    [GeneratedRegex(@"^[0-9A-Za-z.+\-]{1,50}$")]
    private static partial Regex VersionValida();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Correo();

    /// <summary>Tokens, JWT y enlaces de un solo uso: 32 o más caracteres seguidos de base64url.</summary>
    [GeneratedRegex(@"[A-Za-z0-9_\-]{32,}")]
    private static partial Regex Token();

    /// <summary>Seis o más dígitos (cédulas, teléfonos), salvo los de línea y columna de la pila (después de «:»).</summary>
    [GeneratedRegex(@"(?<![:\d])\d{6,}")]
    private static partial Regex NumeroLargo();
}
