using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Modulos.Arquitectura.Datos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Modulos.Arquitectura.Drift;

/// <summary>Sin datos no es lo mismo que sin drift: la API lo dice por capa y por comparación (ATLAS-ADR-0056 §6).</summary>
internal static class EstadosCapa
{
    public const string ConDatos = "con_datos";
    public const string SinDatos = "sin_datos";
    public const string ConDrift = "con_drift";
    public const string SinDrift = "sin_drift";
}

[Description("Si una capa tiene datos para comparar.")]
public sealed record EstadoCapa(
    [property: Description("con_datos o sin_datos.")] string Estado,
    [property: Description("Por qué no hay datos: sin_publicaciones, sin_snapshot, sin_configurar, sin_lectura, lectura_vencida, http_401, sin_respuesta…")] string? Motivo);

[Description("La lectura de Argo CD de un clúster.")]
public sealed record LecturaCluster(string Cluster, [property: Description("con_datos o sin_datos.")] string Estado, string? Motivo, DateTimeOffset? LeidaEn);

[Description("Las tres capas de un ambiente y lo que dicen sus comparaciones.")]
public sealed record EstadoAmbiente(
    string Ambiente,
    EstadoCapa Modelo,
    EstadoCapa Gitops,
    EstadoCapa Argo,
    IReadOnlyList<LecturaCluster> Clusteres,
    [property: Description("con_drift, sin_drift o sin_datos: sin datos nunca se reporta como sin drift.")] string ModeloGitops,
    [property: Description("con_drift, sin_drift o sin_datos: sin datos nunca se reporta como sin drift.")] string GitopsArgo);

[Description("Un hallazgo de drift. No lleva hosts ni IP.")]
public sealed record HallazgoResumen(
    Guid Id,
    [property: Description("modelo_gitops o gitops_argo.")] string Capa,
    string Ambiente,
    string? Cluster,
    string Codigo,
    string? ElementoId,
    string? Sujeto,
    string Descripcion,
    [property: Description("media o alta.")] string Severidad,
    [property: Description("abierto o resuelto.")] string Estado,
    DateTimeOffset DetectadoEn,
    DateTimeOffset? ResueltoEn);

[Description("Panel de drift.")]
public sealed record PanelDrift(IReadOnlyList<EstadoAmbiente> Ambientes, IReadOnlyList<HallazgoResumen> Hallazgos);

/// <summary><c>GET /api/v1/arquitectura/drift</c> (HU 6.5): solo con <c>atlas.arquitectura.ver_despliegue</c>.</summary>
internal static class EndpointsDrift
{
    private const int LimiteHallazgos = 500;

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/drift", PanelAsync)
            .RequierePermiso(PermisosArquitectura.VerDespliegue)
            .WithName("ObtenerPanelDrift")
            .WithSummary("Obtener el panel de drift")
            .WithDescription(
                "Por ambiente, si cada capa (modelo, gitops y Argo CD) tiene datos, qué dice cada comparación y sus hallazgos, hasta 500. " +
                "Una capa sin datos se reporta como sin_datos, nunca como sin_drift. Por defecto, solo los hallazgos abiertos.");
    }

    private static async Task<Ok<PanelDrift>> PanelAsync(
        string? ambiente, string? estado, ClaimsPrincipal usuario, ArquitecturaDbContext contexto, IPermisosDeSesion permisosDeSesion,
        Drift.ILectorArgoCd argo, IOptions<OpcionesArquitectura> opciones, TimeProvider reloj, CancellationToken ct)
    {
        var acceso = await AccesoArquitectura.CalcularAsync(usuario, permisosDeSesion, ct);
        var hallazgos = acceso.Filtrar(contexto.Hallazgos.AsNoTracking());
        if (ambiente is not null)
        {
            hallazgos = hallazgos.Where(h => h.Ambiente == ambiente);
        }
        hallazgos = estado == "resuelto" ? hallazgos.Where(h => h.Estado == EstadoHallazgo.Resuelto) : hallazgos.Where(h => h.Estado == EstadoHallazgo.Abierto);
        var filas = await hallazgos.OrderByDescending(h => h.Severidad == SeveridadDrift.Alta).ThenByDescending(h => h.DetectadoEn).Take(LimiteHallazgos).ToListAsync(ct);

        var ambientes = new List<EstadoAmbiente>();
        foreach (var nombre in Drift.ServicioDrift.Ambientes.Where(a => ambiente is null || a == ambiente))
        {
            ambientes.Add(await EstadoDeAsync(nombre, contexto, acceso, argo, opciones.Value, reloj, ct));
        }
        return TypedResults.Ok(new PanelDrift(ambientes,
        [
            .. filas.Select(h => new HallazgoResumen(h.Id, ReglasArquitectura.Texto(h.Capa), h.Ambiente, h.Cluster, h.Codigo, h.ElementoId, h.Sujeto, h.Descripcion,
                ReglasArquitectura.Texto(h.Severidad), ReglasArquitectura.Texto(h.Estado), h.DetectadoEn, h.ResueltoEn)),
        ]));
    }

    private static async Task<EstadoAmbiente> EstadoDeAsync(
        string ambiente, ArquitecturaDbContext contexto, AccesoArquitectura acceso, Drift.ILectorArgoCd argo, OpcionesArquitectura opciones, TimeProvider reloj, CancellationToken ct)
    {
        var publicaciones = contexto.Publicaciones.AsNoTracking().Where(p => p.Canal == ambiente);
        var hayModelo = await publicaciones.AnyAsync(p => p.Tipo == TipoPublicacion.Modelo, ct);
        var clusteres = await publicaciones.Where(p => p.Tipo == TipoPublicacion.Despliegue).Select(p => p.Cluster).Distinct().OrderBy(c => c).ToListAsync(ct);
        var lecturas = await contexto.LecturasArgo.AsNoTracking().Where(l => clusteres.Contains(l.Cluster)).ToDictionaryAsync(l => l.Cluster, ct);

        var porCluster = clusteres.Select(cluster =>
        {
            if (!argo.Configurado(cluster))
            {
                return new LecturaCluster(cluster, EstadosCapa.SinDatos, "sin_configurar", null);
            }
            if (!lecturas.TryGetValue(cluster, out var lectura))
            {
                return new LecturaCluster(cluster, EstadosCapa.SinDatos, "sin_lectura", null);
            }
            if (lectura.Resultado != ResultadoLecturaArgo.Correcta)
            {
                return new LecturaCluster(cluster, EstadosCapa.SinDatos, lectura.Motivo ?? "error", lectura.LeidaEn);
            }
            // Una lectura vieja no dice nada de ahora (el trabajo pudo morir): tampoco cuenta como datos.
            return reloj.GetUtcNow() - lectura.LeidaEn > opciones.IntervaloDrift * 2
                ? new LecturaCluster(cluster, EstadosCapa.SinDatos, "lectura_vencida", lectura.LeidaEn)
                : new LecturaCluster(cluster, EstadosCapa.ConDatos, null, lectura.LeidaEn);
        }).ToList();

        var comparada = await contexto.Comparaciones.AsNoTracking().Where(c => c.Ambiente == ambiente).Select(c => (DateTimeOffset?)c.ComparadaEn).FirstOrDefaultAsync(ct);
        var comparacionVigente = comparada is { } cuando && reloj.GetUtcNow() - cuando <= opciones.IntervaloDrift * 2;

        var abiertos = acceso.Filtrar(contexto.Hallazgos.AsNoTracking()).Where(h => h.Ambiente == ambiente && h.Estado == EstadoHallazgo.Abierto);
        var hayModeloGitops = await abiertos.AnyAsync(h => h.Capa == CapaDrift.ModeloGitops, ct);
        var hayGitopsArgo = await abiertos.AnyAsync(h => h.Capa == CapaDrift.GitopsArgo, ct);

        var gitopsConDatos = clusteres.Count > 0;
        var argoConDatos = porCluster.Count > 0 && porCluster.All(c => c.Estado == EstadosCapa.ConDatos);
        var motivoArgo = porCluster.Count == 0 ? "sin_snapshot" : porCluster.FirstOrDefault(c => c.Estado == EstadosCapa.SinDatos)?.Motivo;

        return new EstadoAmbiente(
            ambiente,
            new EstadoCapa(hayModelo ? EstadosCapa.ConDatos : EstadosCapa.SinDatos, hayModelo ? null : "sin_publicaciones"),
            new EstadoCapa(gitopsConDatos ? EstadosCapa.ConDatos : EstadosCapa.SinDatos, gitopsConDatos ? null : "sin_snapshot"),
            new EstadoCapa(argoConDatos ? EstadosCapa.ConDatos : EstadosCapa.SinDatos, argoConDatos ? null : motivoArgo),
            porCluster,
            Comparacion(hayModeloGitops, comparacionVigente),
            Comparacion(hayGitopsArgo, argoConDatos));
    }

    /// <summary>Lo abierto se muestra aunque hoy falten datos; sin datos y sin nada abierto, se dice «sin datos», no «sin drift».</summary>
    private static string Comparacion(bool hayAbiertos, bool hayDatos) =>
        hayAbiertos ? EstadosCapa.ConDrift : hayDatos ? EstadosCapa.SinDrift : EstadosCapa.SinDatos;
}
