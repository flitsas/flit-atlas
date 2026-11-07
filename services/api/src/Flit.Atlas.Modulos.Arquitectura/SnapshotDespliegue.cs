using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Flit.Atlas.Modulos.Arquitectura.Datos;

namespace Flit.Atlas.Modulos.Arquitectura;

[Description("Imagen de un contenedor, sin credenciales ni registro interno.")]
public sealed record ImagenWorkload(string Imagen, [property: Description("sha256:…, si está fijada por digest.")] string? Digest);

[Description("Un workload del clúster.")]
public sealed record WorkloadSnapshot(string Kind, string Nombre, IReadOnlyList<ImagenWorkload> Imagenes, int? Replicas);

[Description("Un namespace y lo que corre en él.")]
public sealed record NamespaceSnapshot(
    string Nombre,
    [property: Description("Application de Argo CD que lo sincroniza.")] string? Application,
    IReadOnlyList<WorkloadSnapshot> Workloads,
    [property: Description("Solo hosts públicos; los internos y las IP no llegan aquí.")] IReadOnlyList<string> IngressHosts);

[Description("El clúster del snapshot.")]
public sealed record ClusterSnapshot(string Id, string? Raiz, IReadOnlyList<NamespaceSnapshot> Namespaces);

[Description("Un servicio que corre fuera de k3s, declarado a mano en gitops.")]
public sealed record ServicioFueraDeK3s(string Nombre, string? Metodo, string? ElementoModelo, string? VerificadoEn);

[Description("Un workload que el drift no debe señalar, con su motivo y su fecha de revisión.")]
public sealed record ExclusionDrift(string Patron, string Motivo, [property: Description("dd/mm/aaaa no: AAAA-MM-DD. Vencida, la exclusión deja de aplicar.")] string? RevisarEn);

[Description("Lo desplegado en un ambiente y un clúster según un commit de flit-gitops. No lleva secretos, IP ni hosts internos.")]
public sealed record SnapshotDespliegue(
    string Ambiente,
    [property: Description("Si viene, debe ser el sha del token OIDC.")] string? GitopsSha,
    DateTimeOffset? GeneradoEn,
    ClusterSnapshot Cluster,
    IReadOnlyList<ServicioFueraDeK3s>? FueraDeK3s,
    IReadOnlyList<ExclusionDrift>? Ignorar);

/// <summary>
/// Validación estricta del snapshot (ATLAS-ADR-0056 §5): un campo desconocido se rechaza, así que un generador viejo o mal escrito no
/// puede colar variables de entorno ni anotaciones. Lo que se guarda es lo que se leyó, reescrito por el servidor.
/// </summary>
internal static partial class ReglasSnapshot
{
    public const int MaximoNamespaces = 200;
    public const int MaximoWorkloads = 2000;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9.-]{0,251}[a-z0-9])?$", RegexOptions.CultureInvariant)]
    private static partial Regex NombreKubernetes();

    /// <summary>Imagen con etiqueta opcional, sin digest (<c>ghcr.io/org/app:v1</c>). Admite <c>:</c> en cualquier tramo: un registro con puerto lo rechaza <see cref="RegistroPublico"/> como sensible.</summary>
    [GeneratedRegex(@"^[a-z0-9][A-Za-z0-9._/:-]{0,300}$", RegexOptions.CultureInvariant)]
    private static partial Regex NombreImagen();

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Digest();

    [GeneratedRegex(@"^[A-Za-z0-9*._/-]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronIgnorar();

    private static readonly HashSet<string> KindsPermitidos = new(["Deployment", "StatefulSet", "DaemonSet", "CronJob"], StringComparer.Ordinal);

    public static (SnapshotDespliegue? Snapshot, string? Codigo, IReadOnlyList<string> Errores) Leer(JsonElement cuerpo, string ambiente, string cluster, string commit)
    {
        SnapshotDespliegue? leido;
        try
        {
            leido = cuerpo.Deserialize<SnapshotDespliegue>(Json);
        }
        catch (JsonException ex)
        {
            // La ruta del campo sirve para corregir el generador; JsonException no incluye el valor.
            return (null, "snapshot_invalido", [$"El snapshot no tiene el formato esperado ({ex.Path ?? "$"})."]);
        }
        if (leido is null || leido.Cluster is null)
        {
            return (null, "snapshot_invalido", ["El snapshot debe traer su clúster."]);
        }

        var errores = new List<string>();
        var sensibles = new List<string>();
        if (leido.Ambiente != ambiente)
        {
            errores.Add("El ambiente del snapshot no coincide con el de la publicación.");
        }
        if (leido.Cluster.Id != cluster)
        {
            errores.Add("El clúster del snapshot no coincide con el de la publicación.");
        }
        if (leido.GitopsSha is { } sha && !string.Equals(sha, commit, StringComparison.OrdinalIgnoreCase))
        {
            errores.Add("El sha del snapshot no es el commit que publica.");
        }
        var namespaces = leido.Cluster.Namespaces ?? [];
        if (namespaces.Count > MaximoNamespaces || namespaces.Sum(n => n.Workloads?.Count ?? 0) > MaximoWorkloads)
        {
            errores.Add($"El snapshot pasa de {MaximoNamespaces} namespaces o de {MaximoWorkloads} workloads.");
        }
        foreach (var espacio in namespaces)
        {
            if (!EsNombre(espacio.Nombre) || (espacio.Application is { } app && !EsNombre(app)))
            {
                errores.Add("Un namespace o una Application tienen un nombre inválido.");
            }
            foreach (var host in espacio.IngressHosts ?? [])
            {
                if (!ReglasArquitectura.HostPublico(host ?? string.Empty))
                {
                    // Sin el valor: si es un host interno, no debe quedar en los logs del workflow.
                    sensibles.Add($"{espacio.Nombre}: un ingressHost es una IP o un host interno.");
                }
            }
            foreach (var carga in espacio.Workloads ?? [])
            {
                if (!KindsPermitidos.Contains(carga.Kind ?? string.Empty) || !EsNombre(carga.Nombre))
                {
                    errores.Add($"{espacio.Nombre}: hay un workload con tipo o nombre inválido.");
                }
                foreach (var imagen in carga.Imagenes ?? [])
                {
                    if (imagen.Imagen is null || !NombreImagen().IsMatch(imagen.Imagen) || (imagen.Digest is { } d && !Digest().IsMatch(d)))
                    {
                        errores.Add($"{espacio.Nombre}/{carga.Nombre}: hay una imagen inválida.");
                    }
                    else if (ReglasArquitectura.FormaSensible(imagen.Imagen) is not null || !RegistroPublico(imagen.Imagen))
                    {
                        sensibles.Add($"{espacio.Nombre}/{carga.Nombre}: la imagen apunta a una IP o a un registro interno.");
                    }
                }
            }
        }
        foreach (var servicio in leido.FueraDeK3s ?? [])
        {
            foreach (var texto in new[] { servicio.Nombre, servicio.Metodo, servicio.ElementoModelo, servicio.VerificadoEn })
            {
                if (texto is { Length: > 200 } || (texto is not null && ReglasArquitectura.FormaSensible(texto) is not null))
                {
                    sensibles.Add("fueraDeK3s: un campo parece una IP o un secreto, o es demasiado largo.");
                }
            }
        }
        foreach (var exclusion in leido.Ignorar ?? [])
        {
            if (exclusion.Patron is null || !PatronIgnorar().IsMatch(exclusion.Patron) || string.IsNullOrWhiteSpace(exclusion.Motivo) || exclusion.Motivo.Length > 500
                || (exclusion.RevisarEn is { } r && !DateOnly.TryParseExact(r, "yyyy-MM-dd", out _)))
            {
                errores.Add("ignorar: cada exclusión lleva patrón, motivo y fecha AAAA-MM-DD.");
            }
            else if (ReglasArquitectura.FormaSensible(exclusion.Motivo) is not null)
            {
                sensibles.Add("ignorar: un motivo parece una IP o un secreto.");
            }
        }
        if (sensibles.Count > 0)
        {
            return (null, "contenido_sensible", [.. sensibles.Distinct().Take(20)]);
        }
        return errores.Count > 0 ? (null, "snapshot_invalido", [.. errores.Distinct().Take(20)]) : (leido, null, []);
    }

    private static bool EsNombre(string? nombre) => nombre is not null && NombreKubernetes().IsMatch(nombre);

    /// <summary>Si la imagen nombra un registro (primer tramo con punto o puerto), debe ser un host público.</summary>
    private static bool RegistroPublico(string imagen)
    {
        var barra = imagen.IndexOf('/', StringComparison.Ordinal);
        if (barra <= 0)
        {
            return true;
        }
        var registro = imagen[..barra];
        if (!registro.Contains('.', StringComparison.Ordinal) && !registro.Contains(':', StringComparison.Ordinal))
        {
            return true;
        }
        var sinPuerto = registro.Split(':')[0];
        return ReglasArquitectura.HostPublico(sinPuerto) && !registro.Contains(':', StringComparison.Ordinal);
    }

    /// <summary>El contenido que se guarda y el texto sobre el que va el hash, que no cuenta <c>generadoEn</c>: republicar el mismo sha da «igual».</summary>
    public static (string Contenido, string Sha256) Canonico(SnapshotDespliegue snapshot)
    {
        var contenido = JsonSerializer.Serialize(snapshot, Json);
        return (contenido, ReglasArquitectura.Sha256(JsonSerializer.Serialize(snapshot with { GeneradoEn = null }, Json)));
    }

    public static SnapshotDespliegue Parsear(string contenido) =>
        JsonSerializer.Deserialize<SnapshotDespliegue>(contenido, Json) ?? throw new InvalidOperationException("Snapshot guardado inválido.");

    /// <summary>Diff entre dos snapshots: workloads, imágenes, réplicas y hosts (plan 01 §9.3).</summary>
    public static DiffArquitectura Diff(SnapshotDespliegue antes, SnapshotDespliegue despues)
    {
        ArgumentNullException.ThrowIfNull(antes);
        ArgumentNullException.ThrowIfNull(despues);
        static Dictionary<string, WorkloadSnapshot> Cargas(SnapshotDespliegue s) =>
            (s.Cluster.Namespaces ?? []).SelectMany(n => (n.Workloads ?? []).Select(w => (Clave: $"{n.Nombre}/{w.Kind}/{w.Nombre}", w)))
                .GroupBy(x => x.Clave, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().w, StringComparer.Ordinal);
        static HashSet<string> Hosts(SnapshotDespliegue s) =>
            (s.Cluster.Namespaces ?? []).SelectMany(n => n.IngressHosts ?? []).ToHashSet(StringComparer.Ordinal);

        var a = Cargas(antes);
        var b = Cargas(despues);
        var altas = b.Keys.Except(a.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(k => new CambioArquitectura("workload", k, Imagenes(b[k]))).ToList();
        var bajas = a.Keys.Except(b.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(k => new CambioArquitectura("workload", k, Imagenes(a[k]))).ToList();
        var cambios = new List<CambioArquitectura>();
        foreach (var clave in a.Keys.Intersect(b.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var (x, y) = (a[clave], b[clave]);
            if (Imagenes(x) != Imagenes(y))
            {
                cambios.Add(new CambioArquitectura("imagen", clave, Imagenes(y)));
            }
            if (x.Replicas != y.Replicas)
            {
                cambios.Add(new CambioArquitectura("replicas", clave, $"{x.Replicas?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"} → {y.Replicas?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}"));
            }
        }
        var (ha, hb) = (Hosts(antes), Hosts(despues));
        altas.AddRange(hb.Except(ha, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(h => new CambioArquitectura("host", h, null)));
        bajas.AddRange(ha.Except(hb, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(h => new CambioArquitectura("host", h, null)));
        return new DiffArquitectura(
            [.. altas.Take(ReglasArquitectura.MaximoCambiosDiff)], [.. bajas.Take(ReglasArquitectura.MaximoCambiosDiff)], [.. cambios.Take(ReglasArquitectura.MaximoCambiosDiff)]);
    }

    private static string Imagenes(WorkloadSnapshot w) =>
        string.Join(", ", (w.Imagenes ?? []).Select(i => i.Digest is null ? i.Imagen : $"{i.Imagen}@{i.Digest}").Order(StringComparer.Ordinal));

    public static VisibilidadArquitectura? Visibilidad(string? texto) => ReglasArquitectura.LeerVisibilidad(texto);
}
