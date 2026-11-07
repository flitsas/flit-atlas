using System.ComponentModel;
using Flit.Atlas.Modulos.Gia.Datos;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Tablero con los KPI del plan 02 §15 que Atlas puede calcular (HU 8.6, ATLAS-ADR-0055 §9). No se envía a nadie.</summary>
internal static class EndpointsTablero
{
    private const string SinGateway = "Sin datos hasta tener el gateway.";

    [Description("Un KPI del plan 02 §15.")]
    public sealed record Kpi(
        string Codigo, string Nombre,
        [property: Description("false si Atlas no tiene con qué calcularlo: nunca se muestra como cero.")] bool Disponible,
        [property: Description("Lo que cumple.")] int? Valor,
        [property: Description("Sobre cuánto se mide.")] int? Total,
        [property: Description("Meta de la Fase 4 del plan.")] string Meta,
        [property: Description("Por qué no hay dato, o aclaración del cálculo.")] string? Nota);

    [Description("Un conteo por categoría.")]
    public sealed record Conteo(string Clave, int Total);

    [Description("El tablero de gobierno de IA.")]
    public sealed record Tablero(
        IReadOnlyList<Conteo> CasosPorEstado,
        [property: Description("Nivel de la evaluación aprobada o, si no la hay, el estimado por el plan.")] IReadOnlyList<Conteo> CasosPorRiesgo,
        int RevisionesVencidas,
        IReadOnlyList<Kpi> Kpis);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/tablero", TableroAsync)
            .RequierePermiso(PermisosGia.Ver)
            .WithName("ObtenerTableroIa").WithSummary("Tablero de gobierno de IA")
            .WithDescription("Casos por estado y riesgo, revisiones vencidas y los KPI del plan 02 §15. Los que dependen del gateway o de otros sistemas responden disponible=false. Requiere atlas.gia.ver.")
            .Produces<Tablero>();
    }

    private static async Task<IResult> TableroAsync(GiaDbContext contexto, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var casos = await contexto.Casos.AsNoTracking().ToListAsync(ct);
        var eipds = await contexto.Eipds.AsNoTracking().Where(e => e.Estado == ValoresGia.EipdAprobada).Select(e => e.CasoUsoId).ToListAsync(ct);
        var hoy = EndpointsCasos.Hoy(reloj);
        var ahora = reloj.GetUtcNow();
        var vigentes = casos.Where(c => c.Estado != ValoresGia.EstadoRetirado).ToList();

        static string Riesgo(CasoUso c) => c.NivelRiesgo ?? c.RiesgoPreliminar ?? "sin_evaluar";
        static bool EsAlto(CasoUso c) => Riesgo(c) is ValoresGia.NivelAlto or ValoresGia.NivelCritico;
        static bool EsMedioOMas(CasoUso c) => Riesgo(c) != ValoresGia.NivelBajo && Riesgo(c) != "sin_evaluar";
        static bool Aprobado(CasoUso c) => c.Estado is ValoresGia.EstadoAprobado or ValoresGia.EstadoEnProduccion;

        var altosConPii = vigentes.Where(c => EsAlto(c) && ReglasGia.TieneDatosPersonales(c)).ToList();
        var medioOMas = vigentes.Where(EsMedioOMas).ToList();

        var empleados = await plataforma.Personas.AsNoTracking().CountAsync(p => p.Tipo == TipoPersona.Empleado && p.Estado == EstadoPersona.Activa, ct);
        var ia01 = await contexto.Politicas.AsNoTracking().FirstOrDefaultAsync(p => p.Codigo == "POL-SGSI-IA-01" && p.Estado == ValoresGia.PoliticaVigente, ct);
        var aceptaron = ia01 is null ? 0 : await contexto.Aceptaciones.AsNoTracking().CountAsync(a => a.PoliticaId == ia01.Id, ct);

        var asientos = await contexto.Asientos.AsNoTracking().Where(a => a.RetiradoEn == null).Select(a => a.ConfiguracionVerificadaEn).ToListAsync(ct);
        var limite = ahora.AddDays(-30);
        var incidentesViejos = await contexto.Incidentes.AsNoTracking()
            .CountAsync(i => (i.Severidad == "S1" || i.Severidad == "S2") && i.CerradoEn == null && i.DetectadoEn < limite, ct);

        IReadOnlyList<Kpi> kpis =
        [
            new("trafico_gateway", "% del tráfico LLM de pdn por el gateway", false, null, null, "100 %", SinGateway),
            new("llaves_fuera", "Llaves de proveedor fuera del gateway", false, null, null, "0", SinGateway),
            new("costo", "Desviación del costo mensual frente al presupuesto", false, null, null, "≤ 10 %", SinGateway),
            new("revocar_llave", "Tiempo para revocar una llave expuesta", false, null, null, "< 15 min", "Se mide con un simulacro, fuera de Atlas."),
            new("secretos_repos", "Secretos reales en repos", false, null, null, "0", "Lo mide gitleaks en cada repo, fuera de Atlas."),
            new("gitleaks_ci", "Repos con gitleaks en CI", false, null, null, "100 %", "Sale de GitHub, fuera de Atlas."),
            new("casos_aprobados", "Casos de uso con ficha aprobada", true, vigentes.Count(Aprobado), vigentes.Count, "100 %", null),
            new("altos_con_eipd", "Casos altos con datos personales y EIPD aprobada", true, altosConPii.Count(c => eipds.Contains(c.Id)), altosConPii.Count, "100 %",
                "Incluye el riesgo estimado por el plan en los casos sin evaluación."),
            new("evals_declarados", "Casos medio o más con evals declarados en la ficha", true, medioOMas.Count(c => !string.IsNullOrWhiteSpace(c.EvalsRuta)), medioOMas.Count, "100 %",
                "Es lo declarado en la ficha; Atlas no comprueba que corran en la CI."),
            ia01 is null
                ? new("politica_ia01", "Personas con POL-SGSI-IA-01 aceptada", false, null, empleados, "100 %", "La política todavía no está vigente.")
                : new("politica_ia01", "Personas con POL-SGSI-IA-01 aceptada", true, aceptaron, empleados, "100 %", null),
            asientos.Count == 0
                ? new("configuracion_verificada", "Asientos con configuración verificada", false, null, null, "100 %", "No hay asientos registrados: falta la encuesta del plan 02 §12.1.")
                : new("configuracion_verificada", "Asientos con configuración verificada", true, asientos.Count(a => a is not null), asientos.Count, "100 %", null),
            new("incidentes_viejos", "Incidentes S1 o S2 sin cerrar hace más de 30 días", true, incidentesViejos, null, "0", null),
        ];

        return Results.Ok(new Tablero(
            [.. casos.GroupBy(c => c.Estado).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new Conteo(g.Key, g.Count()))],
            [.. vigentes.GroupBy(Riesgo).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new Conteo(g.Key, g.Count()))],
            vigentes.Count(c => c.ProximaRevision < hoy),
            kpis));
    }
}
