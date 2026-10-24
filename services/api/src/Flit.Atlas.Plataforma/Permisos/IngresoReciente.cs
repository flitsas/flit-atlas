using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Permisos;

/// <summary>Metadato de los endpoints que exigen reautenticación (lo lee el contrato OpenAPI para documentar el 401).</summary>
public sealed class MetadatoReautenticacion;

/// <summary>
/// Reautenticación para acciones sensibles (prompt maestro §6.3, ATLAS-ADR-0022): la implementa la identidad con el
/// <c>auth_time</c> de la sesión. Vive en el núcleo para que los módulos, que no referencian la identidad
/// (ATLAS-ADR-0016), la exijan en sus endpoints (p. ej. exportar paquetes, HU 4.5).
/// </summary>
public interface IReautenticacion
{
    /// <summary>¿El último ingreso de la sesión está dentro de la ventana?</summary>
    bool EsReciente(HttpContext http);

    /// <summary>Registra el rechazo y responde 401 con <c>codigo = "reautenticacion_requerida"</c>.</summary>
    Task<IResult> RechazarAsync(HttpContext http);
}

public static class ExtensionesIngresoReciente
{
    public const string CodigoReautenticacion = "reautenticacion_requerida";

    /// <summary>
    /// Exige un ingreso reciente en el endpoint, como <c>RequiereReautenticacion</c> de la identidad. Sin implementación
    /// registrada (un host sin identidad), rechaza: falla cerrado.
    /// </summary>
    public static TBuilder ExigeIngresoReciente<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder
            .WithMetadata(new MetadatoReautenticacion())
            .AddEndpointFilter(async (contexto, siguiente) =>
            {
                var http = contexto.HttpContext;
                var reautenticacion = http.RequestServices.GetService<IReautenticacion>();
                if (reautenticacion is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Vuelve a ingresar para confirmar esta acción.",
                        extensions: new Dictionary<string, object?> { ["codigo"] = CodigoReautenticacion });
                }
                return reautenticacion.EsReciente(http) ? await siguiente(contexto).ConfigureAwait(false) : await reautenticacion.RechazarAsync(http).ConfigureAwait(false);
            });
}
