using Flit.Atlas.Modulos.Equipo.Dominio;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Equipo.Tests;

/// <summary>
/// HU 2.3 (ATLAS-ADR-0031): lectura del documento de responsables, enlace con el catálogo y las personas, y las tablas
/// 2, 3.1 y 3.2 reconstruidas exactamente desde los datos. Con el documento ficticio de la misma estructura.
/// </summary>
public class DocumentoResponsablesTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 23);

    [Fact]
    public void Lee_el_encabezado_las_definiciones_y_las_tres_tablas()
    {
        var documento = DocumentoResponsables.Leer(DocumentoFicticio.Texto);

        Assert.Empty(documento.Errores);
        Assert.Equal(5, documento.Version);
        Assert.Equal(new DateOnly(2026, 10, 20), documento.Fecha);
        Assert.Equal("Persona Demo Uno (Líder técnico)", documento.DefinidoPor);
        Assert.Equal("reparto definido por el líder técnico", documento.DescripcionVersion);
        Assert.Equal(new DateOnly(2027, 1, 20), documento.ProximaRevision);
        Assert.Equal(20, documento.DedicacionSecundario);
        Assert.Equal(7, documento.PorPersona.Count);
        Assert.Equal(9, documento.Productos.Count);
        Assert.Equal(17, documento.Modulos.Count);
        Assert.Equal(["Firmas", "Sentinel", "Atlas", "flit-scheduler", "Plataforma"], documento.PorPersona[0].PrincipalDe);
        Assert.Equal(new FilaAplicativo("Vigía", "Persona Demo Tres", "Persona Demo Uno"), documento.Productos[0]);
    }

    [Fact]
    public void Las_definiciones_de_cada_rol_quedan_tal_cual()
    {
        var documento = DocumentoResponsables.Leer(DocumentoFicticio.Texto);

        var principal = Assert.Single(documento.Definiciones, d => d.Rol == "principal");
        Assert.Equal("Responsable principal", principal.Titulo);
        Assert.Equal(
        [
            "Dueño técnico del producto o módulo: backlog técnico, calidad, decisiones de diseño, despliegues y atención de incidentes.",
            "Revisa (o delega la revisión de) todo PR que toque su módulo.",
            "Mantiene al día la documentación (README, manual de operación, ADRs del módulo).",
        ], principal.Puntos);
        var secundario = Assert.Single(documento.Definiciones, d => d.Rol == "secundario");
        Assert.Equal("Responsable secundario", secundario.Titulo);
        Assert.Equal("Dedicación orientativa: ~20 % de su tiempo en ese módulo.", secundario.Puntos[^1]);
        // Cada punto del documento está igual en las definiciones.
        Assert.All(documento.Definiciones.SelectMany(d => d.Puntos), p => Assert.Contains("- " + p + "\n", DocumentoFicticio.Texto.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void Un_documento_sin_estructura_dice_que_le_falta()
    {
        var documento = DocumentoResponsables.Leer("# Otra cosa\n\nNada que ver.");

        Assert.Null(documento.Version);
        Assert.Equal(4, documento.Errores.Count);
    }

    [Fact]
    public void CA_las_tablas_2_3_1_y_3_2_se_reproducen_exactamente_desde_los_datos()
    {
        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(DocumentoFicticio.Texto), DocumentoFicticio.Catalogo(),
            DocumentoFicticio.Candidatas(septimaSinTilde: false), Hoy);
        Assert.True(plan.PuedeConfirmar, string.Join('\n', plan.Pendientes.Select(p => p.Detalle)));
        Assert.Empty(plan.Inconsistencias);

        // Los datos pasan por la foto que guarda cada versión (JSON) antes de volver a ser tablas.
        var tablas = TablasReparto.Generar(FotoReparto.DesdeJson(plan.Foto.AJson()));

        Assert.Equal(DocumentoFicticio.Tabla("## 2."), tablas.Personas);
        Assert.Equal(DocumentoFicticio.Tabla("### 3.1"), tablas.Productos);
        Assert.Equal(DocumentoFicticio.Tabla("### 3.2"), tablas.Modulos);
        Assert.Null(tablas.Servicios);
        Assert.Equal(52, plan.Foto.Responsabilidades.Count);
    }

    [Fact]
    public void Enlaza_personas_sin_tildes_ni_mayusculas_y_con_la_dedicacion_del_secundario()
    {
        var personas = DocumentoFicticio.Candidatas(septimaSinTilde: true);
        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(DocumentoFicticio.Texto), DocumentoFicticio.Catalogo(), personas, Hoy);

        Assert.Empty(plan.Pendientes);
        var septima = personas[6];
        Assert.Equal(5, plan.Foto.Responsabilidades.Count(r => r.PersonaId == septima.Id));
        Assert.All(plan.Foto.Responsabilidades, r => Assert.Equal(r.Tipo == "secundario" ? 20 : null, r.Dedicacion));
        Assert.All(plan.Foto.Responsabilidades, r => Assert.Equal(new DateOnly(2026, 10, 20), r.VigenteDesde));
    }

    [Fact]
    public void Lo_que_no_se_puede_enlazar_queda_en_pendientes_y_no_se_inventa()
    {
        var catalogo = DocumentoFicticio.Catalogo().Where(a => a.Slug != "sentinel").ToList();
        var personas = DocumentoFicticio.Candidatas().Where(p => p.Nombre != "Persona Demo Dos").ToList();
        // Dos empleados con el mismo nombre: ambiguo.
        personas.Add(new PersonaCandidata(Guid.CreateVersion7(), "Persona Demo Cinco", "otra.cinco@ejemplo.com", true));

        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(DocumentoFicticio.Texto), catalogo, personas, Hoy);

        Assert.False(plan.PuedeConfirmar);
        Assert.Contains(plan.Pendientes, p => p is { Tipo: "aplicativo", Nombre: "Sentinel", Motivo: "no_encontrado" });
        Assert.Contains(plan.Pendientes, p => p is { Tipo: "persona", Nombre: "Persona Demo Dos", Motivo: "no_encontrado" });
        Assert.Contains(plan.Pendientes, p => p is { Tipo: "persona", Nombre: "Persona Demo Cinco", Motivo: "ambiguo" });
        // Una sola vez por nombre, aunque aparezca en varias filas.
        Assert.Single(plan.Pendientes, p => p.Nombre == "Persona Demo Dos");
        Assert.DoesNotContain(plan.Foto.Responsabilidades, r => r.Slug == "sentinel");
    }

    [Fact]
    public void La_tabla_2_inconsistente_con_las_tablas_3_queda_reportada()
    {
        var texto = DocumentoFicticio.Texto.Replace("| **Persona Demo Tres** | Vigía, Infraestructura |", "| **Persona Demo Tres** | Vigía |", StringComparison.Ordinal);

        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(texto), DocumentoFicticio.Catalogo(), DocumentoFicticio.Candidatas(), Hoy);

        var inconsistencia = Assert.Single(plan.Inconsistencias);
        Assert.Contains("Persona Demo Tres", inconsistencia, StringComparison.Ordinal);
        Assert.True(plan.PuedeConfirmar);
    }

    [Fact]
    public void La_huella_cambia_si_cambia_el_reparto_y_no_si_cambian_los_nombres()
    {
        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(DocumentoFicticio.Texto), DocumentoFicticio.Catalogo(), DocumentoFicticio.Candidatas(), Hoy);
        var foto = plan.Foto;
        var renombrada = new FotoReparto([.. foto.Responsabilidades.Select(r => r with { Persona = r.Persona.ToUpperInvariant(), Aplicativo = "x" })]);
        var otra = new FotoReparto([.. foto.Responsabilidades.Select((r, i) => i == 0 ? r with { Dedicacion = 30 } : r)]);

        Assert.Equal(foto.Huella(), renombrada.Huella());
        Assert.NotEqual(foto.Huella(), otra.Huella());
        Assert.Equal(64, plan.Huella().Length);
    }

    [Fact]
    public void Normaliza_tildes_mayusculas_negritas_y_espacios()
    {
        Assert.Equal("persona demo sebastian", Textos.Normalizar("  **Persona   Demo SEBASTIÁN** "));
        Assert.Equal("persona demo nuno", Textos.Normalizar("Persona Demo Nuño"));
        Assert.Equal(string.Empty, Textos.Normalizar(null));
    }

    [Fact]
    public void El_mapa_de_carga_resalta_a_quien_se_sale_de_la_media()
    {
        var plan = PlanImportacion.Armar(DocumentoResponsables.Leer(DocumentoFicticio.Texto), DocumentoFicticio.Catalogo(),
            DocumentoFicticio.Candidatas(septimaSinTilde: false), Hoy);

        var (media, personas) = MapaCarga.Calcular(plan.Foto);

        Assert.Equal(4.46m, media);
        var seis = Assert.Single(personas, p => p.Persona == "Persona Demo Seis");
        Assert.Equal((6, 4, 6.8m, "alta"), (seis.Principales, seis.Secundarias, seis.Puntaje, seis.Nivel));
        var tres = Assert.Single(personas, p => p.Persona == "Persona Demo Tres");
        Assert.Equal((2, 3, 2.6m, "baja"), (tres.Principales, tres.Secundarias, tres.Puntaje, tres.Nivel));
        Assert.Equal("Persona Demo Seis", personas[0].Persona);
        Assert.Equal("normal", Assert.Single(personas, p => p.Persona == "Persona Demo Séptima").Nivel);
    }

    [Fact]
    public void Un_servicio_con_responsables_propios_va_en_su_tabla()
    {
        var aplicativo = new AplicativoLeido(Guid.CreateVersion7(), "core-demo", "core-demo", "servicio", null, "flit2", "activo", null, null);
        var foto = new FotoReparto([new ResponsabilidadFoto(aplicativo.Id, aplicativo.Slug, aplicativo.Nombre, "servicio", "principal", Guid.CreateVersion7(),
            "Persona Demo", null, Hoy, null, 1, 1, 1)]);

        var tablas = TablasReparto.Generar(foto);

        Assert.Equal("| Servicio | Principal | Secundario |\n|---|---|---|\n| core-demo | Persona Demo | — |\n", tablas.Servicios);
        Assert.Equal("| Producto | Principal | Secundario |\n|---|---|---|\n", tablas.Productos);
    }
}
