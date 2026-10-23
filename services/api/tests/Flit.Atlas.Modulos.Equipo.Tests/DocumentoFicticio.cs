using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Equipo.Tests;

/// <summary>
/// El documento de responsables ficticio (<c>Datos/responsables-ficticio.md</c>): la estructura exacta de la versión 5
/// con personas «Persona Demo». Los productos y módulos son los del catálogo de FLIT (datos de la empresa, sin personas).
/// </summary>
internal static class DocumentoFicticio
{
    public static string Texto => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Datos", "responsables-ficticio.md"));

    public static readonly string[] Personas =
    [
        "Persona Demo Uno", "Persona Demo Dos", "Persona Demo Tres", "Persona Demo Cuatro", "Persona Demo Cinco", "Persona Demo Seis", "Persona Demo Séptima",
    ];

    public static readonly Guid Flit2 = Guid.CreateVersion7();

    private static readonly (string Slug, string Nombre, string? Repo)[] Productos =
    [
        ("vigia", "Vigía", "flitsas/flit-infra-studio"), ("certify", "Certify", "flitsas/flit-certify"), ("flito", "FLITO", "flitsas/flito"),
        ("firmas", "Firmas", "flitsas/flit-firmas"), ("sentinel", "Sentinel", null), ("atlas", "Atlas", "flitsas/flit-atlas"),
        ("people", "People", null), ("flit-scheduler", "flit-scheduler", "flitsas/flit-scheduler"), ("infraestructura", "Infraestructura", "flitsas/flit-gitops"),
    ];

    private static readonly (string Slug, string Nombre)[] Modulos =
    [
        ("flit2-tramites", "Trámites"), ("flit2-diagnostico", "Diagnóstico"), ("flit2-comparendos", "Comparendos"), ("flit2-administradores", "Administradores"),
        ("flit2-usuarios", "Usuarios"), ("flit2-identidad", "Identidad"), ("flit2-suite", "Suite"), ("flit2-migracion", "Migración"),
        ("flit2-consultas", "Consultas"), ("flit2-notificaciones", "Notificaciones"), ("flit2-ict", "ICT"), ("flit2-integraciones", "Integraciones"),
        ("flit2-log", "Log"), ("flit2-reportes", "Reportes"), ("flit2-ayuda", "Ayuda"), ("flit2-doctor-flit", "Doctor FLIT"), ("flit2-plataforma", "Plataforma"),
    ];

    /// <summary>El catálogo: los 9 productos, FLIT 2.0 con sus 17 módulos y un servicio con carpeta bajo Comparendos.</summary>
    public static List<AplicativoLeido> Catalogo()
    {
        var lista = new List<AplicativoLeido> { new(Flit2, "flit2", "FLIT 2.0", "producto", null, "flit2", "activo", "flitsas/flit", null) };
        lista.AddRange(Productos.Select(p => new AplicativoLeido(Guid.CreateVersion7(), p.Slug, p.Nombre, "producto", null, "satelite", "activo", p.Repo, null)));
        lista.AddRange(Modulos.Select(m => new AplicativoLeido(Guid.CreateVersion7(), m.Slug, m.Nombre, "modulo", Flit2, "flit2", "activo", null, null)));
        var comparendos = lista.Single(a => a.Slug == "flit2-comparendos");
        lista.Add(new AplicativoLeido(Guid.CreateVersion7(), "core-comparendos", "core-comparendos", "servicio", comparendos.Id, "flit2", "activo", null, "services/core-comparendos"));
        return lista;
    }

    /// <summary>Los empleados con los nombres del documento; la séptima, escrita sin tilde (se enlaza igual).</summary>
    public static List<PersonaCandidata> Candidatas(bool septimaSinTilde = true) =>
    [
        .. Personas.Select((n, i) => new PersonaCandidata(Guid.CreateVersion7(),
            septimaSinTilde && i == 6 ? "persona demo SEPTIMA" : n, $"persona.{i + 1}@ejemplo.com", true)),
    ];

    /// <summary>Las líneas de la tabla que sigue al título que empieza por <paramref name="titulo"/>, como están en el documento.</summary>
    public static string Tabla(string titulo)
    {
        var lineas = Texto.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inicio = Array.FindIndex(lineas, l => l.StartsWith(titulo, StringComparison.Ordinal));
        Assert.True(inicio >= 0, titulo);
        return string.Concat(lineas.Skip(inicio + 1).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')).Select(l => l + "\n"));
    }
}
