using System.Globalization;
using System.Text.Json;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Guias;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Poda;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Modulos.Contratos.Specs;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Contratos.Exportaciones;

// --- «Paquete a exportar»: el modelo PaqueteExportar de atlas-docs (services/docs/src/atlas_docs/exportes/modelo.py) ---

internal sealed record EntradaChangelog(string Revision, DateOnly Fecha, string Cambio);

internal sealed record MetadatosEntrada(string Paquete, int Revision, string Organizacion, DateOnly Fecha, IReadOnlyList<EntradaChangelog> Changelog);

internal sealed record SpecEntrada(string Contrato, string Version, string Tipo, string Contenido);

internal sealed record GuiaEntrada(string Titulo, string Markdown);

internal sealed record EntradaExportacion(MetadatosEntrada Metadatos, IReadOnlyList<SpecEntrada> Specs, IReadOnlyList<GuiaEntrada> Guias)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public byte[] AJson() => JsonSerializer.SerializeToUtf8Bytes(this, Json);
}

/// <summary>Lo que se exporta, ya armado, con su nombre para mostrar.</summary>
internal sealed record EntradaArmada(EntradaExportacion Entrada, string Nombre, Guid? ContratoId);

/// <summary>No se puede exportar: código estable y el detalle para la persona.</summary>
internal sealed class NoExportableException(string mensaje, IReadOnlyList<string>? detalles = null) : Exception(mensaje)
{
    public IReadOnlyList<string> Detalles { get; } = detalles ?? [];
}

/// <summary>
/// Arma el «paquete a exportar» que recibe atlas-docs (ATLAS-ADR-0042): para un paquete, los specs de su revisión
/// <b>podados</b> a la selección (HU 4.2, ATLAS-ADR-0038; nunca el spec completo), las guías de la revisión y el changelog
/// del paquete; para una versión (solo internos), el spec completo, las guías publicadas que le aplican y el changelog
/// de la CI. Aplica los mismos topes que valida atlas-docs. La organización y la fecha de Bogotá llegan resueltas.
/// </summary>
internal static class ArmadorEntrada
{
    // Los de atlas-docs (modelo.py): más allá, atlas-docs rechazaría la entrada.
    public const int MaximoSpecs = 30;
    public const int MaximoGuias = 50;
    public const int MaximoChangelog = 500;
    private const int MaximoTexto = 300;
    private const int MaximoCambio = 2000;

    public const string OrganizacionInterna = "FLIT S.A.S. · uso interno";

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    public static DateOnly FechaBogota(DateTimeOffset momento) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(momento, Bogota).DateTime);

    /// <exception cref="NoExportableException"/>
    public static async Task<EntradaArmada> DePaqueteAsync(
        ContratosDbContext contexto, PaqueteContratos paquete, RevisionPaquete revision, string organizacion, DateTimeOffset ahora,
        string ambiente, Func<Contrato, IReadOnlyList<Uri>> servidores, CancellationToken ct)
    {
        var definicion = FotoPaquete.LeerDefinicion(revision.Definicion).Where(e => e.Operaciones.Count > 0).ToList();
        if (definicion.Count == 0)
        {
            throw new NoExportableException($"La revisión {revision.Numero} no tiene operaciones que exportar.");
        }
        if (definicion.Count > MaximoSpecs)
        {
            throw new NoExportableException($"Una exportación lleva hasta {MaximoSpecs} contratos; la revisión {revision.Numero} tiene {definicion.Count}.");
        }
        var idsVersiones = definicion.Select(e => e.VersionId).ToList();
        var versiones = await contexto.Versiones.AsNoTracking().Where(v => idsVersiones.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var idsContratos = definicion.Select(e => e.ContratoId).ToList();
        var contratos = await contexto.Contratos.AsNoTracking().Where(c => idsContratos.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var problemas = new List<string>();
        var specs = new List<SpecEntrada>();
        foreach (var e in definicion.OrderBy(e => contratos.GetValueOrDefault(e.ContratoId)?.Codigo ?? "~", StringComparer.Ordinal))
        {
            var contrato = contratos.GetValueOrDefault(e.ContratoId);
            var version = versiones.GetValueOrDefault(e.VersionId);
            if (contrato is null || version is null || version.ContratoId != contrato.Id)
            {
                problemas.Add($"{e.ContratoId}: el contrato o su versión ya no existe.");
                continue;
            }
            if (Podadores.Para(contrato.Tipo) is not { } podador)
            {
                problemas.Add($"{contrato.Codigo}: los contratos {Valores.Texto(contrato.Tipo)} no se exportan.");
                continue;
            }
            try
            {
                var destino = new DestinoPoda(ambiente, servidores(contrato));
                var podado = podador.Podar(new SolicitudPoda(version.Contenido, e.Operaciones, destino));
                specs.Add(new SpecEntrada(contrato.Codigo, version.Version, Valores.Texto(contrato.Tipo), podado.Contenido));
            }
            catch (PodaException ex)
            {
                // Fail-closed (ATLAS-ADR-0038): si no se puede podar, no se exporta nada.
                problemas.Add($"{contrato.Codigo} {version.Version}: {ex.Codigo} ({ex.Message})");
            }
        }

        var guiasPedidas = definicion.SelectMany(e => e.Guias).ToList();
        if (guiasPedidas.Count > MaximoGuias)
        {
            problemas.Add($"Una exportación lleva hasta {MaximoGuias} guías; la revisión tiene {guiasPedidas.Count}.");
        }
        if (problemas.Count > 0)
        {
            throw new NoExportableException($"La revisión {revision.Numero} no se puede exportar: {problemas[0]}", problemas);
        }

        var idsGuias = guiasPedidas.Select(g => g.GuiaId).Distinct().ToList();
        var revisionesGuia = (await contexto.RevisionesGuia.AsNoTracking().Where(r => idsGuias.Contains(r.GuiaId)).ToListAsync(ct))
            .ToDictionary(r => (r.GuiaId, r.Numero));
        var guias = new List<GuiaEntrada>();
        foreach (var e in definicion)
        {
            foreach (var g in e.Guias)
            {
                if (revisionesGuia.TryGetValue((g.GuiaId, g.Revision), out var r))
                {
                    guias.Add(new GuiaEntrada(Recortar(r.Titulo, MaximoTexto), r.Contenido));
                }
            }
        }

        var changelog = await ChangelogPaqueteAsync(contexto, paquete.Id, revision.Numero, ct);
        var metadatos = new MetadatosEntrada(Recortar(revision.Nombre, MaximoTexto), revision.Numero, Recortar(organizacion, MaximoTexto),
            FechaBogota(ahora), changelog);
        return new EntradaArmada(new EntradaExportacion(metadatos, specs, guias), $"{revision.Nombre} · revisión {revision.Numero}", null);
    }

    /// <summary>
    /// Un paquete del portal (HU 4.4 y 4.5): solo lo que la organización ve hoy (<see cref="AccesoPortal"/>): la revisión de
    /// su compartición, los contratos técnicos y disponibles con su spec podado por <see cref="SpecsDelPortal"/> (el mismo
    /// que muestra el portal, con su caché) y las guías visibles.
    /// </summary>
    /// <exception cref="NoExportableException"/>
    public static async Task<EntradaArmada> DePortalAsync(
        ContratosDbContext contexto, SpecsDelPortal specsDelPortal, PaqueteDelPortal paquete, string organizacion, DateTimeOffset ahora, CancellationToken ct)
    {
        var contratos = paquete.Contratos.Where(c => c.Tecnico && c.Entrada.Operaciones.Count > 0).OrderBy(c => c.Contrato.Codigo, StringComparer.Ordinal).ToList();
        if (contratos.Count == 0 || contratos.Count > MaximoSpecs)
        {
            throw new NoExportableException("El paquete no tiene contratos que exportar.");
        }
        var problemas = new List<string>();
        var specs = new List<SpecEntrada>();
        foreach (var c in contratos)
        {
            var resultado = await specsDelPortal.PodarAsync(paquete, c, null, ct).ConfigureAwait(false);
            if (resultado.Spec is null)
            {
                problemas.Add($"{c.Contrato.Codigo}: {resultado.Codigo}");
                continue;
            }
            specs.Add(new SpecEntrada(c.Contrato.Codigo, c.Version.Version, Valores.Texto(c.Contrato.Tipo), resultado.Spec.Contenido));
        }
        var pedidas = contratos.SelectMany(c => c.Guias).ToList();
        if (pedidas.Count > MaximoGuias)
        {
            problemas.Add($"más de {MaximoGuias} guías");
        }
        if (problemas.Count > 0)
        {
            throw new NoExportableException($"El paquete no se puede exportar: {problemas[0]}", problemas);
        }
        var ids = pedidas.Select(g => g.GuiaId).Distinct().ToList();
        var cuerpos = (await contexto.RevisionesGuia.AsNoTracking().Where(r => ids.Contains(r.GuiaId)).ToListAsync(ct).ConfigureAwait(false))
            .ToDictionary(r => (r.GuiaId, r.Numero));
        var guias = pedidas
            .Where(g => cuerpos.ContainsKey((g.GuiaId, g.Revision)))
            .Select(g => new GuiaEntrada(Recortar(g.Titulo, MaximoTexto), cuerpos[(g.GuiaId, g.Revision)].Contenido))
            .ToList();
        var revision = paquete.RevisionPaquete;
        var changelog = await ChangelogPaqueteAsync(contexto, paquete.Paquete.Id, revision.Numero, ct).ConfigureAwait(false);
        var metadatos = new MetadatosEntrada(Recortar(revision.Nombre, MaximoTexto), revision.Numero, Recortar(organizacion, MaximoTexto), FechaBogota(ahora), changelog);
        return new EntradaArmada(new EntradaExportacion(metadatos, specs, guias), $"{revision.Nombre} · revisión {revision.Numero}", null);
    }

    /// <summary>Una versión completa (solo internos): el spec tal como se publicó, sin podar.</summary>
    /// <exception cref="NoExportableException"/>
    public static async Task<EntradaArmada> DeVersionAsync(
        ContratosDbContext contexto, Contrato contrato, VersionContrato version, DateTimeOffset ahora, CancellationToken ct)
    {
        if (contrato.Tipo is not (TipoContrato.Openapi or TipoContrato.Asyncapi))
        {
            throw new NoExportableException($"Los contratos {Valores.Texto(contrato.Tipo)} no se exportan.");
        }
        var semver = VersionSemantica.Leer(version.Version);
        var guias = (await contexto.Guias.AsNoTracking()
                .Where(g => g.ContratoId == contrato.Id && g.RevisionPublicada != null)
                .Join(contexto.RevisionesGuia.AsNoTracking(), g => new { GuiaId = g.Id, Numero = g.RevisionPublicada!.Value },
                    r => new { r.GuiaId, r.Numero }, (g, r) => r)
                .ToListAsync(ct))
            .Where(r => semver is null || ReglasGuias.AplicaA(r.AplicaDesde, r.AplicaHasta, semver))
            .OrderBy(r => r.Titulo, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.GuiaId)
            .Take(MaximoGuias)
            .Select(r => new GuiaEntrada(Recortar(r.Titulo, MaximoTexto), r.Contenido))
            .ToList();
        var nombre = $"{contrato.Codigo} {version.Version}";
        var metadatos = new MetadatosEntrada(Recortar($"{contrato.Nombre} {version.Version}", MaximoTexto), 1, OrganizacionInterna,
            FechaBogota(ahora), ChangelogVersion(version));
        return new EntradaArmada(
            new EntradaExportacion(metadatos, [new SpecEntrada(contrato.Codigo, version.Version, Valores.Texto(contrato.Tipo), version.Contenido)], guias),
            nombre, contrato.Id);
    }

    /// <summary>
    /// El changelog del paquete hasta la revisión exportada, de la más reciente a la más antigua: qué contratos entraron o
    /// salieron, qué versiones cambiaron y cuántas operaciones y guías se agregaron o quitaron. Se genera de las
    /// definiciones; la nota de cada revisión no sale (es texto libre interno y puede traer datos personales).
    /// </summary>
    internal static async Task<IReadOnlyList<EntradaChangelog>> ChangelogPaqueteAsync(ContratosDbContext contexto, Guid paqueteId, int hasta, CancellationToken ct)
    {
        var revisiones = await contexto.RevisionesPaquete.AsNoTracking()
            .Where(r => r.PaqueteId == paqueteId && r.Numero <= hasta)
            .OrderBy(r => r.Numero)
            .Select(r => new { r.Numero, r.Definicion, r.GuardadaEn })
            .ToListAsync(ct);
        var definiciones = revisiones.Select(r => FotoPaquete.LeerDefinicion(r.Definicion)).ToList();
        var idsContratos = definiciones.SelectMany(d => d.Select(e => e.ContratoId)).Distinct().ToList();
        var idsVersiones = definiciones.SelectMany(d => d.Select(e => e.VersionId)).Distinct().ToList();
        var codigos = await contexto.Contratos.AsNoTracking().Where(c => idsContratos.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Codigo, ct);
        var numeros = await contexto.Versiones.AsNoTracking().Where(v => idsVersiones.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Version, ct);

        var entradas = new List<EntradaChangelog>();
        for (var i = 0; i < revisiones.Count; i++)
        {
            var cambio = i == 0
                ? Primera(definiciones[0])
                : Diferencias(definiciones[i - 1], definiciones[i], codigos, numeros);
            entradas.Add(new EntradaChangelog(revisiones[i].Numero.ToString(CultureInfo.InvariantCulture), FechaBogota(revisiones[i].GuardadaEn),
                Recortar(cambio, MaximoCambio)));
        }
        entradas.Reverse();
        return [.. entradas.Take(MaximoChangelog)];
    }

    private static string Primera(IReadOnlyList<EntradaPaquete> d) =>
        $"Primera revisión del paquete: {Cantidad(d.Count, "contrato", "contratos")}, {Cantidad(d.Sum(e => e.Operaciones.Count), "operación", "operaciones")} "
        + $"y {Cantidad(d.Sum(e => e.Guias.Count), "guía", "guías")}.";

    private static string Diferencias(
        IReadOnlyList<EntradaPaquete> antes, IReadOnlyList<EntradaPaquete> despues, IReadOnlyDictionary<Guid, string> codigos,
        IReadOnlyDictionary<Guid, string> numeros)
    {
        string Codigo(Guid id) => codigos.GetValueOrDefault(id) ?? "contrato retirado";
        string Numero(Guid id) => numeros.GetValueOrDefault(id) ?? "?";
        var previos = antes.ToDictionary(e => e.ContratoId);
        var actuales = despues.ToDictionary(e => e.ContratoId);
        var frases = new List<string>();
        foreach (var e in despues.Where(e => !previos.ContainsKey(e.ContratoId)))
        {
            frases.Add($"Se agrega {Codigo(e.ContratoId)} {Numero(e.VersionId)} con {Cantidad(e.Operaciones.Count, "operación", "operaciones")}.");
        }
        foreach (var e in antes.Where(e => !actuales.ContainsKey(e.ContratoId)))
        {
            frases.Add($"Se quita {Codigo(e.ContratoId)}.");
        }
        foreach (var e in despues.Where(e => previos.ContainsKey(e.ContratoId)))
        {
            var p = previos[e.ContratoId];
            var partes = new List<string>();
            if (p.VersionId != e.VersionId)
            {
                partes.Add($"pasa de la versión {Numero(p.VersionId)} a la {Numero(e.VersionId)}");
            }
            var agregadas = e.Operaciones.Except(p.Operaciones, StringComparer.Ordinal).Count();
            var quitadas = p.Operaciones.Except(e.Operaciones, StringComparer.Ordinal).Count();
            if (agregadas > 0)
            {
                partes.Add($"se agregan {Cantidad(agregadas, "operación", "operaciones")}");
            }
            if (quitadas > 0)
            {
                partes.Add($"se quitan {Cantidad(quitadas, "operación", "operaciones")}");
            }
            var guiasAntes = p.Guias.Select(g => (g.GuiaId, g.Revision)).ToHashSet();
            var guiasDespues = e.Guias.Select(g => (g.GuiaId, g.Revision)).ToHashSet();
            if (!guiasAntes.SetEquals(guiasDespues))
            {
                partes.Add("cambian sus guías");
            }
            if (partes.Count > 0)
            {
                frases.Add($"{Codigo(e.ContratoId)}: {string.Join(", ", partes)}.");
            }
        }
        return frases.Count > 0 ? string.Join(" ", frases) : "Cambios en el nombre o la descripción del paquete.";
    }

    /// <summary>El changelog que calculó la CI con oasdiff (su campo <c>text</c>), o la publicación si no trae.</summary>
    internal static IReadOnlyList<EntradaChangelog> ChangelogVersion(VersionContrato version)
    {
        var fecha = FechaBogota(version.PublicadoEn);
        var entradas = new List<EntradaChangelog>();
        try
        {
            using var json = JsonDocument.Parse(version.Changelog);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var cambio in json.RootElement.EnumerateArray())
                {
                    if (cambio.ValueKind == JsonValueKind.Object && cambio.TryGetProperty("text", out var texto) && texto.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(texto.GetString()))
                    {
                        entradas.Add(new EntradaChangelog(version.Version, fecha, Recortar(texto.GetString()!, MaximoCambio)));
                    }
                    if (entradas.Count == MaximoChangelog)
                    {
                        break;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Un changelog ilegible no impide exportar: queda solo la entrada de la publicación.
        }
        if (entradas.Count == 0)
        {
            entradas.Add(new EntradaChangelog(version.Version, fecha, $"Versión {version.Version} publicada desde {version.Repositorio}@{version.Rama}."));
        }
        return entradas;
    }

    private static string Cantidad(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";

    private static string Recortar(string texto, int largo)
    {
        var limpio = texto.Trim();
        return limpio.Length <= largo ? limpio : limpio[..(largo - 1)] + "…";
    }
}
