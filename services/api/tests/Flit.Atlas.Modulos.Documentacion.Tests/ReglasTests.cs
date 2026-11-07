namespace Flit.Atlas.Modulos.Documentacion.Tests;

/// <summary>Validador de MDX y lector de front-matter, sin base (ATLAS-ADR-0053 §4 y §5).</summary>
public class ReglasTests
{
    [Theory]
    [InlineData("# Título\n\nTexto normal con **negrita** y [enlace](https://ejemplo.com).")]
    [InlineData("[interno](/catalogo/flito) y [ancla](#seccion) y [correo](mailto:equipo@ejemplo.com)")]
    [InlineData("<Contrato id=\"external-api\" operacion=\"crearTramite\" />")]
    [InlineData("<Alerta tipo='aviso'>\nTexto dentro\n</Alerta>")]
    [InlineData("<Pestanas>\n<Alerta tipo=\"x\">a</Alerta>\n</Pestanas>")]
    [InlineData("<SoporteDeTurno />")]
    [InlineData("Un `{codigo}` y `<script>` en línea no cuentan.")]
    [InlineData("```js\nimport x from 'y';\nconst a = { b: 1 };\n<script>alert(1)</script>\n```")]
    [InlineData("~~~\n{sin problema}\n~~~")]
    [InlineData("Llaves escapadas \\{ así \\} y menor \\< que.")]
    [InlineData("Una palabra: importante, exportar.")]
    public void Admite(string texto) => Assert.Empty(ValidadorMdx.Validar(texto));

    [Theory]
    [InlineData("import x from 'y'", "import")]
    [InlineData("export const a = 1", "import")]
    [InlineData("Valor: {1 + 1}", "llaves")]
    [InlineData("Cierre suelto }", "llaves")]
    [InlineData("<script>alert(1)</script>", "lista blanca")]
    [InlineData("<div>hola</div>", "lista blanca")]
    [InlineData("<img src=x onerror=alert(1)>", "lista blanca")]
    [InlineData("<Otro />", "lista blanca")]
    [InlineData("<contrato id=\"x\" />", "lista blanca")]
    [InlineData("<Contrato.Malo />", "lista blanca")]
    [InlineData("a < b", "suelto")]
    [InlineData("<!-- comentario -->", "suelto")]
    [InlineData("<>fragmento</>", "suelto")]
    [InlineData("<Contrato id={valor} />", "cadenas literales")]
    [InlineData("<Contrato bandera />", "cadenas literales")]
    [InlineData("<Contrato {...props} />", "cadenas literales")]
    [InlineData("<Alerta tipo=\"x\">sin cerrar", "falta cerrar")]
    [InlineData("</Alerta>", "no cierra")]
    [InlineData("<Alerta tipo=\"x", "comilla")]
    [InlineData("[x](javascript:alert(1))", "enlace")]
    [InlineData("[x](JaVaScRiPt:alert(1))", "enlace")]
    [InlineData("[x](data:text/html,hola)", "enlace")]
    [InlineData("[x](http://ejemplo.com)", "enlace")]
    [InlineData("[x](javascript&#58;alert(1))", "enlace")]
    [InlineData("[x](//ejemplo.com)", "enlace")]
    [InlineData("![x](data:image/png;base64,AAAA)", "enlace")]
    [InlineData("[ref]: javascript:alert(1)", "enlace")]
    [InlineData("`\n\n<script>alert(1)</script>\n\n`", "lista blanca")]
    [InlineData("`\n# <script>\n`", "lista blanca")]
    [InlineData("`sin cerrar {x}", "llaves")]
    [InlineData("    ```\n{x}\n    ```", "llaves")]
    public void Rechaza(string texto, string motivo)
    {
        var errores = ValidadorMdx.Validar(texto);

        Assert.NotEmpty(errores);
        Assert.Contains(errores, e => e.Contains(motivo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Dice_la_linea_del_problema()
    {
        var errores = ValidadorMdx.Validar("uno\n\n```\n{bien}\n```\n\nmal {x}");

        Assert.Equal("Línea 7: las expresiones entre llaves no se admiten; escríbelas como \\{ o en código.", errores[0]);
    }

    private const string Frente = """
        ---
        aplicativo: flito
        tipo: guia_funcional # un comentario
        titulo: "Alta de vehículo: paso a paso"
        visibilidad: interno_negocio
        audiencias: [Todos, 'Gestor']
        responsable: Persona Demo
        estado: aprobado
        fuentes_codigo:
          - apps/api/alta.ts
          - apps/api/baja.ts
        ---

        # Cuerpo
        """;

    [Fact]
    public void Lee_el_front_matter()
    {
        var (archivo, errores) = FrontMatter.Leer(Frente);

        Assert.Empty(errores);
        Assert.Equal("flito", archivo!.Texto("aplicativo"));
        Assert.Equal("guia_funcional", archivo.Texto("tipo"));
        Assert.Equal("Alta de vehículo: paso a paso", archivo.Texto("titulo"));
        Assert.Equal(["Todos", "Gestor"], archivo.Lista("audiencias"));
        Assert.Equal(["apps/api/alta.ts", "apps/api/baja.ts"], archivo.Lista("fuentes_codigo"));
        Assert.Equal("# Cuerpo", archivo.Cuerpo);
    }

    [Theory]
    [InlineData("# Sin front-matter", "empezar")]
    [InlineData("---\naplicativo: flito\n", "no se cierra")]
    [InlineData("---\naplicativo: flito\n---\nx", "falta «tipo»")]
    [InlineData("---\naplicativo: a\naplicativo: b\ntipo: t\ntitulo: t\nvisibilidad: v\naudiencias: []\nresponsable: r\n---\nx", "repetida")]
    [InlineData("---\naplicativo: &ancla a\ntipo: t\ntitulo: t\nvisibilidad: v\naudiencias: []\nresponsable: r\n---\nx", "no se admite")]
    [InlineData("---\naplicativo: a\ntipo: t\ntitulo: t\nvisibilidad: v\naudiencias: []\nresponsable: r\nmapa:\n  clave: valor\n---\nx", "solo se admite")]
    public void Rechaza_el_front_matter(string texto, string motivo)
    {
        var (archivo, errores) = FrontMatter.Leer(texto);

        Assert.Null(archivo);
        Assert.Contains(errores, e => e.Contains(motivo, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("docs/funcional/flito/alta.md", true)]
    [InlineData("docs/integraciones/guía de uso.mdx", true)]
    [InlineData("../secreto.md", false)]
    [InlineData("docs/../../x.md", false)]
    [InlineData("/etc/x.md", false)]
    [InlineData("docs\\x.md", false)]
    [InlineData("docs/x.txt", false)]
    [InlineData("", false)]
    public void Valida_la_ruta(string ruta, bool valida) => Assert.Equal(valida, ReglasDocumentos.RutaValida(ruta));

    [Fact]
    public void El_mismo_archivo_en_windows_y_en_linux_da_el_mismo_hash()
    {
        var linux = ReglasDocumentos.Sha256(ReglasDocumentos.Normalizar("a\nb"));
        var windows = ReglasDocumentos.Sha256(ReglasDocumentos.Normalizar("﻿a\r\nb"));

        Assert.Equal(linux, windows);
    }
}
