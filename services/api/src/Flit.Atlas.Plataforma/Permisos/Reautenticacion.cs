using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Metadato de los endpoints que exigen reautenticación (lo lee el contrato OpenAPI).</summary>
public sealed class MetadatoReautenticacion;

/// <summary>
/// Reautenticación para acciones sensibles (prompt maestro §6.3, ATLAS-ADR-0022): la implementa la identidad con el
/// <c>auth_time</c> de la sesión. Vive en Plataforma para que los módulos (que no referencian a la identidad, ATLAS-ADR-0016)
/// la exijan en sus endpoints, p. ej. compartir un paquete con una organización (ATLAS-ADR-0043).
/// </summary>
public interface IVerificadorReautenticacion
{
    /// <summary>¿El último ingreso de la sesión está dentro de la ventana de reautenticación?</summary>
    bool EsReciente(HttpContext http);

    /// <summary>Registra el rechazo y arma el 401 con <c>codigo = "reautenticacion_requerida"</c> que la PWA interpreta.</summary>
    Task<IResult> RechazarAsync(HttpContext http);
}

public static class ExtensionesReautenticacion
{
    /// <summary>
    /// Exige un ingreso reciente en el endpoint o en el grupo. Sin una identidad registrada que lo verifique, rechaza
    /// (falla cerrada): una acción sensible nunca pasa por no saber cuándo entró la persona.
    /// </summary>
    public static TBuilder RequiereReautenticacion<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .WithMetadata(new MetadatoReautenticacion())
            .AddEndpointFilter(async (contexto, siguiente) =>
            {
                var http = contexto.HttpContext;
                var verificador = http.RequestServices.GetService<IVerificadorReautenticacion>();
                if (verificador is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Vuelve a ingresar para confirmar esta acción.",
                        extensions: new Dictionary<string, object?> { ["codigo"] = "reautenticacion_requerida" });
                }
                return verificador.EsReciente(http) ? await siguiente(contexto).ConfigureAwait(false) : await verificador.RechazarAsync(http).ConfigureAwait(false);
            });
}
