using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Arquitectura.Datos;

namespace Flit.Atlas.Modulos.Arquitectura.Drift;

/// <summary>Un contenedor del modelo con su llave de drift.</summary>
internal sealed record ElementoEsperado(string ElementoId, string K8sWorkload, string? Ambientes, IReadOnlyList<string> Etiquetas, Guid AplicativoId, VisibilidadArquitectura Visibilidad);

/// <summary>Un hallazgo calculado, antes de guardarse.</summary>
/// <param name="Sujeto">Lo que le da identidad: workload o Application. Nunca un host ni una IP.</param>
internal sealed record HallazgoCalculado(
    CapaDrift Capa, string Ambiente, string? Cluster, string Codigo, string? ElementoId, string Sujeto, string Descripcion, SeveridadDrift Severidad,
    VisibilidadArquitectura Visibilidad, Guid? AplicativoId);

/// <summary>Una Application de Argo CD, solo lo que compara el drift.</summary>
public sealed record AplicacionArgo(string Nombre, string? Sync, string? Salud, DateTimeOffset? ReconciliadaEn, IReadOnlyList<string> Imagenes);

/// <summary>
/// Las comparaciones del drift como funciones puras (plan 01 §11.4, ATLAS-ADR-0056 §6). Reciben lo que ya se leyó y no saben de
/// dónde vino: la decisión de si hay datos para comparar se toma antes de llamarlas, y sin datos no se llama ninguna.
/// </summary>
internal static partial class ReglasDrift
{
    /// <summary>D1 modelo ↔ gitops. Lee todos los snapshots del ambiente: un workload en cualquier clúster cuenta como presente.</summary>
    public static List<HallazgoCalculado> ModeloContraGitops(
        string ambiente, IReadOnlyList<ElementoEsperado> elementos, IReadOnlyList<SnapshotDespliegue> snapshots, DateOnly hoy, Guid? aplicativoDeGitops,
        VisibilidadArquitectura visibilidadDeGitops)
    {
        ArgumentNullException.ThrowIfNull(elementos);
        ArgumentNullException.ThrowIfNull(snapshots);
        var hallazgos = new List<HallazgoCalculado>();
        var cargas = snapshots.SelectMany(s => (s.Cluster.Namespaces ?? []).SelectMany(n => n.Workloads ?? []).Select(w => w.Nombre)).ToHashSet(StringComparer.Ordinal);
        var fueraDeK3s = snapshots.SelectMany(s => s.FueraDeK3s ?? []).Select(f => f.ElementoModelo).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var exclusiones = snapshots.SelectMany(s => s.Ignorar ?? [])
            .Where(e => e.RevisarEn is null || !DateOnly.TryParseExact(e.RevisarEn, "yyyy-MM-dd", out var r) || r >= hoy)
            .Select(e => e.Patron).Distinct(StringComparer.Ordinal).ToList();
        bool Ignorado(string carga) => exclusiones.Any(p => Coincide(p, carga));

        foreach (var elemento in elementos)
        {
            var declarado = DeclaradoEn(elemento.Ambientes, ambiente);
            var presente = cargas.Contains(elemento.K8sWorkload);
            if (declarado && !presente && !Ignorado(elemento.K8sWorkload)
                // Plan 01 §11.4: #objetivo ausente es lo esperado, no drift; un servicio fuera de k3s no aparece en los workloads.
                && !elemento.Etiquetas.Contains("objetivo", StringComparer.Ordinal)
                && !fueraDeK3s.Any(f => f == elemento.ElementoId || elemento.ElementoId.StartsWith(f + ".", StringComparison.Ordinal)))
            {
                hallazgos.Add(new HallazgoCalculado(CapaDrift.ModeloGitops, ambiente, null, "ausente_en_gitops", elemento.ElementoId, elemento.K8sWorkload,
                    $"El modelo declara «{elemento.K8sWorkload}» en {ambiente} y gitops no lo tiene.",
                    elemento.Etiquetas.Contains("critico", StringComparer.Ordinal) ? SeveridadDrift.Alta : SeveridadDrift.Media, elemento.Visibilidad, elemento.AplicativoId));
            }
            else if (!declarado && presente && !Ignorado(elemento.K8sWorkload))
            {
                hallazgos.Add(new HallazgoCalculado(CapaDrift.ModeloGitops, ambiente, null, "fuera_de_ambiente", elemento.ElementoId, elemento.K8sWorkload,
                    $"«{elemento.K8sWorkload}» corre en {ambiente}, pero el modelo no lo declara para ese ambiente.", SeveridadDrift.Media, elemento.Visibilidad, elemento.AplicativoId));
            }
        }
        var conocidos = elementos.Select(e => e.K8sWorkload).ToHashSet(StringComparer.Ordinal);
        foreach (var carga in cargas.Where(c => !conocidos.Contains(c) && !Ignorado(c)).Order(StringComparer.Ordinal))
        {
            hallazgos.Add(new HallazgoCalculado(CapaDrift.ModeloGitops, ambiente, null, "sin_modelo", null, carga,
                $"«{carga}» corre en {ambiente} y ningún elemento del modelo lo declara.", SeveridadDrift.Media, visibilidadDeGitops, aplicativoDeGitops));
        }
        return hallazgos;
    }

    /// <summary>Si se omite <c>ambientes</c>, el elemento debe estar en todos (plan 01 §8.4).</summary>
    public static bool DeclaradoEn(string? ambientes, string ambiente) =>
        string.IsNullOrWhiteSpace(ambientes)
        || ambientes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(ambiente, StringComparer.Ordinal);

    /// <summary>D2 gitops ↔ Argo CD en un clúster. Solo se llama con una lectura correcta.</summary>
    public static List<HallazgoCalculado> GitopsContraArgo(
        string ambiente, SnapshotDespliegue snapshot, IReadOnlyList<AplicacionArgo> aplicaciones, DateTimeOffset ahora, TimeSpan tolerancia, Guid? aplicativo, VisibilidadArquitectura visibilidad)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(aplicaciones);
        var cluster = snapshot.Cluster.Id;
        var hallazgos = new List<HallazgoCalculado>();
        HallazgoCalculado Nuevo(string codigo, string sujeto, string descripcion) =>
            new(CapaDrift.GitopsArgo, ambiente, cluster, codigo, null, sujeto, descripcion, SeveridadDrift.Alta, visibilidad, aplicativo);

        var porNombre = aplicaciones.GroupBy(a => a.Nombre, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var espacio in snapshot.Cluster.Namespaces ?? [])
        {
            if (espacio.Application is not { } nombre)
            {
                continue;
            }
            if (!porNombre.TryGetValue(nombre, out var aplicacion))
            {
                hallazgos.Add(Nuevo("aplicacion_ausente", nombre, $"gitops declara la Application «{nombre}» y Argo CD no la tiene."));
                continue;
            }
            if (!string.Equals(aplicacion.Sync, "Synced", StringComparison.Ordinal))
            {
                hallazgos.Add(Nuevo("sin_sincronizar", nombre, $"La Application «{nombre}» no está sincronizada ({Corto(aplicacion.Sync)})."));
            }
            if (!string.Equals(aplicacion.Salud, "Healthy", StringComparison.Ordinal))
            {
                hallazgos.Add(Nuevo("sin_salud", nombre, $"La Application «{nombre}» no está sana ({Corto(aplicacion.Salud)})."));
            }
            // Ya pasó en la 169: Argo decía «Synced» estando ciego varios días (plan 01 §11.4 y riesgo R6).
            if (aplicacion.ReconciliadaEn is not { } reconciliada || ahora - reconciliada > tolerancia)
            {
                hallazgos.Add(Nuevo("sin_reconciliar", nombre, $"Argo CD lleva más de {(int)tolerancia.TotalMinutes} minutos sin reconciliar la Application «{nombre}»."));
            }
            var vivas = aplicacion.Imagenes.ToHashSet(StringComparer.Ordinal);
            foreach (var carga in espacio.Workloads ?? [])
            {
                foreach (var imagen in (carga.Imagenes ?? []).Where(i => i.Digest is not null))
                {
                    if (vivas.Count > 0 && !vivas.Any(v => v.Contains(imagen.Digest!, StringComparison.Ordinal)))
                    {
                        hallazgos.Add(Nuevo("imagen_distinta", $"{nombre}/{carga.Nombre}", $"La imagen viva de «{carga.Nombre}» no es el digest que declara gitops."));
                    }
                }
            }
        }
        return hallazgos;
    }

    private static string Corto(string? valor) => string.IsNullOrWhiteSpace(valor) ? "sin estado" : EstadoArgo().IsMatch(valor) ? valor : "estado desconocido";

    /// <summary>Lo que dice Argo se muestra solo si parece un estado: la respuesta no se confía como texto para la interfaz.</summary>
    [GeneratedRegex(@"^[A-Za-z]{1,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex EstadoArgo();

    /// <summary>Patrón de <c>drift-ignorar.yaml</c> con <c>*</c>, sin distinguir nada más.</summary>
    public static bool Coincide(string patron, string valor)
    {
        ArgumentNullException.ThrowIfNull(patron);
        var regex = "^" + string.Join(".*", patron.Split('*').Select(Regex.Escape)) + "$";
        return Regex.IsMatch(valor, regex, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    /// <summary>Identidad estable del hallazgo: si se resuelve y reaparece, reabre la misma fila.</summary>
    public static string Clave(HallazgoCalculado h)
    {
        ArgumentNullException.ThrowIfNull(h);
        return ReglasArquitectura.Sha256($"{h.Capa}|{h.Ambiente}|{h.Cluster}|{h.Codigo}|{h.Sujeto}");
    }
}
