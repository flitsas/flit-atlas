using System.Globalization;
using Flit.Atlas.Modulos.Gia.Datos;

namespace Flit.Atlas.Modulos.Gia;

/// <summary>Valores de los campos de texto con CHECK (plan 02 §7.1, §8, §6.6 y §11.2).</summary>
public static class ValoresGia
{
    public const string EstadoBorrador = "borrador";
    public const string EstadoEnRevision = "en_revision";
    public const string EstadoAprobado = "aprobado";
    public const string EstadoEnProduccion = "en_produccion";
    public const string EstadoSuspendido = "suspendido";
    public const string EstadoRetirado = "retirado";
    public static readonly IReadOnlyList<string> EstadosCaso = [EstadoBorrador, EstadoEnRevision, EstadoAprobado, EstadoEnProduccion, EstadoSuspendido, EstadoRetirado];

    public static readonly IReadOnlyList<string> TiposCaso = ["ocr", "chat", "clasificacion", "generacion", "agente", "embeddings", "otro"];
    public static readonly IReadOnlyList<string> UsuariosAfectados = ["interno", "cliente", "ciudadano"];
    public static readonly IReadOnlyList<string> HumanoEnBucle = ["ninguno", "revision_posterior", "aprobacion_previa"];
    public static readonly IReadOnlyList<string> CategoriasDato = ["publico", "interno", "confidencial", "personal", "sensible"];
    public static readonly IReadOnlyList<string> Ambientes = ["dev", "qa", "pdn"];

    public const string NivelBajo = "bajo";
    public const string NivelMedio = "medio";
    public const string NivelAlto = "alto";
    public const string NivelCritico = "critico";
    public static readonly IReadOnlyList<string> Niveles = [NivelBajo, NivelMedio, NivelAlto, NivelCritico];
    public static readonly IReadOnlyList<string> RiesgosPreliminares = [NivelBajo, NivelMedio, "medio_alto", NivelAlto];

    public const string DecisionPendiente = "pendiente";
    public const string DecisionAprobado = "aprobado";
    public const string DecisionAprobadoCondiciones = "aprobado_condiciones";
    public const string DecisionRechazado = "rechazado";
    public static readonly IReadOnlyList<string> Decisiones = [DecisionPendiente, DecisionAprobado, DecisionAprobadoCondiciones, DecisionRechazado];

    public const string RolResponsableIa = "responsable_ia";
    public const string RolOpd = "opd";
    public const string RolComite = "comite";
    public const string RolSeguridad = "seguridad";
    public static readonly IReadOnlyList<string> RolesGobierno = [RolResponsableIa, RolOpd, RolComite, RolSeguridad];

    public const string VotoAprobar = "aprobar";
    public const string VotoRechazar = "rechazar";
    public static readonly IReadOnlyList<string> Votos = [VotoAprobar, VotoRechazar];

    public const string EipdBorrador = "borrador";
    public const string EipdEnRevision = "en_revision";
    public const string EipdAprobada = "aprobada";
    public const string EipdRechazada = "rechazada";
    public static readonly IReadOnlyList<string> EstadosEipd = [EipdBorrador, EipdEnRevision, EipdAprobada, EipdRechazada];

    public const string PoliticaBorrador = "borrador";
    public const string PoliticaVigente = "vigente";
    public const string PoliticaReemplazada = "reemplazada";
    public static readonly IReadOnlyList<string> EstadosPolitica = [PoliticaBorrador, PoliticaVigente, PoliticaReemplazada];

    public static readonly IReadOnlyList<string> EstadosHerramienta = ["aprobada", "en_evaluacion", "prohibida"];
    public static readonly IReadOnlyList<string> TiposCuenta = ["corporativa", "personal_autorizada"];

    public static readonly IReadOnlyList<string> TiposIncidente = ["INC-IA-01", "INC-IA-02", "INC-IA-03", "INC-IA-04", "INC-IA-05", "INC-IA-06", "INC-IA-07"];
    public static readonly IReadOnlyList<string> Severidades = ["S1", "S2", "S3", "S4"];
}

/// <summary>Una fila de la evaluación (plan 02 §8.3). Una dimensión puede tener varios escenarios.</summary>
/// <param name="P2">Probabilidad residual; sin ella, el residual es el inherente.</param>
/// <param name="I2">Impacto residual; sin él, el residual es el inherente.</param>
public sealed record EscenarioRiesgo(string Dimension, string Escenario, int P, int I, string? ControlesExistentes, string? ControlesNuevos, int? P2, int? I2);

/// <summary>Lo que debe cumplir una aprobación según el nivel inherente (plan 02 §8.2, ATLAS-ADR-0055 §5).</summary>
/// <param name="Roles">Roles de gobierno cuyo voto a favor hace falta.</param>
/// <param name="MesesRevision">Meses hasta la próxima revisión.</param>
public sealed record ReglaAprobacion(IReadOnlyList<string> Roles, int MesesRevision);

/// <summary>Reglas puras del gobierno de IA: niveles, votos necesarios y campos obligatorios.</summary>
public static class ReglasGia
{
    public const string MarcaPendiente = "[PENDIENTE DE REDACCIÓN]";

    public static readonly IReadOnlyList<string> DimensionesRiesgo = ["D1", "D2", "D3", "D4", "D5", "D6"];

    /// <summary>1–4 bajo, 5–9 medio, 10–15 alto y 16–25 crítico (plan 02 §8.2).</summary>
    public static string Nivel(int puntaje) => puntaje switch
    {
        <= 4 => ValoresGia.NivelBajo,
        <= 9 => ValoresGia.NivelMedio,
        <= 15 => ValoresGia.NivelAlto,
        _ => ValoresGia.NivelCritico,
    };

    /// <summary>El puntaje inherente y el residual de la <b>peor</b> dimensión, no el promedio (plan 02 §8.1).</summary>
    public static (int Inherente, int Residual) Puntajes(IReadOnlyCollection<EscenarioRiesgo> escenarios)
    {
        ArgumentNullException.ThrowIfNull(escenarios);
        return (escenarios.Max(e => e.P * e.I), escenarios.Max(e => e.P2 is { } p && e.I2 is { } i ? p * i : e.P * e.I));
    }

    /// <summary>Hay datos personales si las categorías incluyen personal o sensible, o si hay tipos de PII.</summary>
    public static bool TieneDatosPersonales(CasoUso caso)
    {
        ArgumentNullException.ThrowIfNull(caso);
        return caso.TiposPii.Count > 0 || caso.CategoriasDato.Contains("personal") || caso.CategoriasDato.Contains("sensible");
    }

    /// <summary>Quién aprueba, por el nivel inherente: alto y crítico van al Comité (plan 02 §8.2 y §8.5).</summary>
    public static ReglaAprobacion Regla(string nivelInherente, bool datosPersonales) => nivelInherente switch
    {
        ValoresGia.NivelBajo => new([ValoresGia.RolResponsableIa], 12),
        ValoresGia.NivelMedio => new(datosPersonales ? [ValoresGia.RolResponsableIa, ValoresGia.RolOpd] : [ValoresGia.RolResponsableIa], 12),
        _ => new([ValoresGia.RolComite], 6),
    };

    /// <summary>Con residual crítico no se despliega (plan 02 §8.2), aunque haya votos.</summary>
    public static bool Bloqueada(string nivelResidual) => nivelResidual == ValoresGia.NivelCritico;

    /// <summary>¿Ya están todos los votos a favor que pide la regla?</summary>
    public static bool Completa(ReglaAprobacion regla, IEnumerable<string> rolesAFavor)
    {
        ArgumentNullException.ThrowIfNull(regla);
        var aFavor = rolesAFavor.ToHashSet(StringComparer.Ordinal);
        return regla.Roles.All(aFavor.Contains);
    }

    /// <summary>Campos obligatorios de la ficha (plan 02 §7.1) que faltan para enviarla a revisión.</summary>
    public static IReadOnlyList<string> FaltantesFicha(CasoUso caso)
    {
        ArgumentNullException.ThrowIfNull(caso);
        var faltan = new List<string>();
        void Exigir(bool cumple, string campo)
        {
            if (!cumple)
            {
                faltan.Add(campo);
            }
        }

        Exigir(caso.DuenoPersonaId is not null, "dueño del caso");
        Exigir(caso.Ambientes.Count > 0, "ambientes habilitados");
        Exigir(caso.Tipo is not null, "tipo");
        Exigir(caso.UsuariosAfectados.Count > 0, "usuarios afectados");
        Exigir(caso.DecisionSobrePersonas is not null, "si produce decisiones sobre personas");
        Exigir(caso.HumanoEnBucle is not null, "humano en el bucle");
        Exigir(caso.CategoriasDato.Count > 0, "categorías de datos");
        Exigir(caso.VolumenMensualEstimado is not null, "volumen estimado");
        Exigir(!string.IsNullOrWhiteSpace(caso.Minimizacion), "minimización aplicada");
        Exigir(caso.PresidioActivo is not null, "si Presidio está activo");
        Exigir(caso.AliasUsados.Count > 0, "alias del gateway");
        Exigir(!string.IsNullOrWhiteSpace(caso.TransferenciaInternacional), "país de procesamiento o transferencia internacional");
        Exigir(!string.IsNullOrWhiteSpace(caso.MetricasCalidad), "métricas de calidad");
        Exigir(!string.IsNullOrWhiteSpace(caso.PromptsUbicacion), "dónde están versionados los prompts");
        Exigir(!string.IsNullOrWhiteSpace(caso.Controles), "controles aplicados");
        if (TieneDatosPersonales(caso))
        {
            Exigir(!string.IsNullOrWhiteSpace(caso.BaseLegal), "base legal");
        }
        if (caso.UsuariosAfectados.Any(u => u != "interno"))
        {
            Exigir(!string.IsNullOrWhiteSpace(caso.AvisoUsuario), "aviso al usuario");
        }
        return faltan;
    }

    /// <summary>Lo que pide el nivel inherente además de la ficha (plan 02 §8.2 y §8.4) y todavía falta para aprobar.</summary>
    public static IReadOnlyList<string> FaltantesNivel(CasoUso caso, string nivelInherente, string? estadoEipd)
    {
        ArgumentNullException.ThrowIfNull(caso);
        var faltan = new List<string>();
        if (nivelInherente == ValoresGia.NivelBajo)
        {
            return faltan;
        }
        if (string.IsNullOrWhiteSpace(caso.EvalsRuta))
        {
            faltan.Add("suite de evals");
        }
        if (nivelInherente is ValoresGia.NivelAlto or ValoresGia.NivelCritico)
        {
            if (TieneDatosPersonales(caso) && estadoEipd != ValoresGia.EipdAprobada)
            {
                faltan.Add("evaluación de impacto en privacidad aprobada");
            }
            if (caso.HumanoEnBucle is null or "ninguno")
            {
                faltan.Add("humano en el bucle");
            }
            if (string.IsNullOrWhiteSpace(caso.AvisoUsuario))
            {
                faltan.Add("aviso al usuario");
            }
            if (string.IsNullOrWhiteSpace(caso.PlanRollback))
            {
                faltan.Add("plan de rollback");
            }
        }
        return faltan;
    }

    /// <summary>Transiciones por fuera de la aprobación. Reabrir (a borrador) obliga a volver a aprobar.</summary>
    public static bool TransicionPermitida(string actual, string destino) => (actual, destino) switch
    {
        (ValoresGia.EstadoAprobado, ValoresGia.EstadoEnProduccion) => true,
        (ValoresGia.EstadoAprobado or ValoresGia.EstadoEnProduccion, ValoresGia.EstadoSuspendido) => true,
        (ValoresGia.EstadoEnRevision or ValoresGia.EstadoAprobado or ValoresGia.EstadoEnProduccion or ValoresGia.EstadoSuspendido, ValoresGia.EstadoBorrador) => true,
        (not ValoresGia.EstadoRetirado, ValoresGia.EstadoRetirado) => true,
        _ => false,
    };

    /// <summary>Código del incidente siguiente: <c>INC-IA-2026-001</c>.</summary>
    public static string CodigoIncidente(int anio, IEnumerable<string> existentes)
    {
        var prefijo = string.Create(CultureInfo.InvariantCulture, $"INC-IA-{anio}-");
        var ultimo = existentes.Where(c => c.StartsWith(prefijo, StringComparison.Ordinal))
            .Select(c => int.TryParse(c.AsSpan(prefijo.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        return string.Create(CultureInfo.InvariantCulture, $"{prefijo}{ultimo + 1:D3}");
    }
}
