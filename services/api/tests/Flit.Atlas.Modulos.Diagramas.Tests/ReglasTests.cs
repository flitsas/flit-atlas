using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Diagramas.Tests;

/// <summary>XML de BPMN y JSON del lienzo (ATLAS-ADR-0054 §4), sin base de datos.</summary>
public class ReglasTests
{
    private const string Dibujo = """<bpmndi:BPMNDiagram id="D"><bpmndi:BPMNPlane id="P" bpmnElement="Proceso"/></bpmndi:BPMNDiagram>""";

    private static string Bpmn(string dentro = "", string antes = "", string atributos = "") => $"""
        {antes}<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:camunda="http://camunda.org/schema/1.0/bpmn" id="Def" targetNamespace="http://ejemplo.com/b" {atributos}>
          <bpmn:process id="Proceso">{dentro}</bpmn:process>
          {Dibujo}
        </bpmn:definitions>
        """;

    [Fact]
    public void Un_bpmn_valido_se_conserva()
    {
        var r = ValidadorBpmn.Validar(Bpmn("""<bpmn:startEvent id="Inicio" name="Radicar"/>"""));

        Assert.Empty(r.Errores);
        Assert.Contains("id=\"Inicio\"", r.Fuente);
        Assert.Contains("name=\"Radicar\"", r.Fuente);
        Assert.Contains("BPMNDiagram", r.Fuente);
    }

    [Theory]
    [InlineData("<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>")]
    [InlineData("<!DOCTYPE foo [<!ENTITY a \"aaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]>")]
    public void Una_dtd_se_rechaza_sin_resolver_nada(string dtd)
    {
        var r = ValidadorBpmn.Validar(Bpmn(antes: dtd));

        Assert.Null(r.Fuente);
        Assert.Contains("DTD", Assert.Single(r.Errores));
    }

    [Theory]
    [InlineData("<a><b></a>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("")]
    public void Lo_que_no_es_bpmn_se_rechaza(string xml) => Assert.Null(ValidadorBpmn.Validar(xml).Fuente);

    [Fact]
    public void Sin_proceso_o_sin_dibujo_se_rechaza()
    {
        const string Raiz = """xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" """;

        Assert.Null(ValidadorBpmn.Validar($"<bpmn:definitions {Raiz}>{Dibujo}</bpmn:definitions>").Fuente);
        Assert.Null(ValidadorBpmn.Validar($"""<bpmn:definitions {Raiz}><bpmn:process id="Proceso"/></bpmn:definitions>""").Fuente);
    }

    [Fact]
    public void La_limpieza_quita_lo_que_no_es_del_estandar_y_el_codigo()
    {
        var r = ValidadorBpmn.Validar(Bpmn(
            """
            <!-- comentario --><?php echo 1?>
            <bpmn:scriptTask id="T" name="Tarea" scriptFormat="javascript" camunda:asyncBefore="true" onclick="alert(1)">
              <bpmn:extensionElements><camunda:executionListener event="start"><camunda:script>evil()</camunda:script></camunda:executionListener></bpmn:extensionElements>
              <bpmn:script>fetch('https://ejemplo.com')</bpmn:script>
            </bpmn:scriptTask>
            <script xmlns="http://www.w3.org/1999/xhtml">alert(2)</script>
            """,
            atributos: "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://ejemplo.com/a http://ejemplo.com/a.xsd\""));

        Assert.Empty(r.Errores);
        var limpio = r.Fuente!;
        Assert.Contains("id=\"T\"", limpio);
        Assert.DoesNotContain("evil", limpio);
        Assert.DoesNotContain("fetch", limpio);
        Assert.DoesNotContain("alert", limpio);
        Assert.DoesNotContain("camunda", limpio);
        Assert.DoesNotContain("comentario", limpio);
        Assert.DoesNotContain("php", limpio);
        Assert.DoesNotContain("schemaLocation", limpio);
        Assert.DoesNotContain("xhtml", limpio);
    }

    [Fact]
    public void Lo_que_escribe_el_editor_de_bpmn_js_se_conserva()
    {
        const string Editor = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" xmlns:dc="http://www.omg.org/spec/DD/20100524/DC" xmlns:di="http://www.omg.org/spec/DD/20100524/DI" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" id="Def" targetNamespace="https://ejemplo.com/b">
              <bpmn:process id="P" isExecutable="false">
                <bpmn:startEvent id="I" name="Inicio"><bpmn:outgoing>F</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="F" sourceRef="I" targetRef="T"><bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">x</bpmn:conditionExpression></bpmn:sequenceFlow>
                <bpmn:task id="T" name="Revisar &amp; aprobar"><bpmn:documentation>Paso</bpmn:documentation><bpmn:incoming>F</bpmn:incoming></bpmn:task>
              </bpmn:process>
              <bpmndi:BPMNDiagram id="D"><bpmndi:BPMNPlane id="Pl" bpmnElement="P">
                <bpmndi:BPMNShape id="I_di" bpmnElement="I"><dc:Bounds x="1" y="2" width="36" height="36"/></bpmndi:BPMNShape>
                <bpmndi:BPMNEdge id="F_di" bpmnElement="F"><di:waypoint x="1" y="2"/><di:waypoint x="3" y="4"/></bpmndi:BPMNEdge>
              </bpmndi:BPMNPlane></bpmndi:BPMNDiagram>
            </bpmn:definitions>
            """;

        var r = ValidadorBpmn.Validar(Editor);

        Assert.Empty(r.Errores);
        foreach (var parte in new[] { "dc:Bounds", "di:waypoint", "xsi:type=\"bpmn:tFormalExpression\"", "Revisar &amp; aprobar", "bpmn:documentation", "isExecutable=\"false\"" })
        {
            Assert.Contains(parte, r.Fuente);
        }
    }

    [Fact]
    public void Limpiar_lo_ya_limpio_no_cambia_nada()
    {
        var una = ValidadorBpmn.Validar(Bpmn("""<bpmn:task id="T"><bpmn:extensionElements/></bpmn:task>""")).Fuente;

        Assert.Equal(una, ValidadorBpmn.Validar(una).Fuente);
    }

    private static readonly Guid Aplicativo = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, AplicativoLeido> Catalogo = new()
    {
        [Aplicativo] = new AplicativoLeido(Aplicativo, "flito", "FLITO", "producto", null, "flito", "activo", null, null),
    };

    [Fact]
    public void Un_lienzo_valido_queda_en_forma_canonica_y_con_la_proyeccion()
    {
        var r = ValidadorLienzo.Validar($$"""
            { "relaciones": [{ "id": "r1", "origen": "a", "destino": "b", "etiqueta": " usa " }],
              "nodos": [
                { "id": "a", "tipo": "sistema", "etiqueta": " Radicación ", "x": 1.26, "y": 2, "aplicativoId": "{{Aplicativo}}" },
                { "id": "b", "tipo": "nota", "etiqueta": "Nota", "x": 0, "y": 0, "elementoLikeC4": "flit2.core_api" } ] }
            """, Catalogo);

        Assert.Empty(r.Errores);
        Assert.Equal(2, r.Lienzo!.Nodos.Count);
        Assert.Equal("Radicación", r.Lienzo.Nodos[0].Etiqueta);
        Assert.Equal(1.3, r.Lienzo.Nodos[0].X);
        Assert.Equal("usa", r.Lienzo.Relaciones[0].Etiqueta);
        Assert.StartsWith("{\"nodos\":", r.Fuente);
        Assert.Equal(r.Fuente, ValidadorLienzo.Validar(r.Fuente, Catalogo).Fuente);
    }

    [Theory]
    [InlineData("no es json")]
    [InlineData("""{"nodos":[],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0}]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0,"onClick":"x"}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0}],"relaciones":[],"extra":1}""")]
    [InlineData("""{"nodos":[{"id":"a/b","tipo":"sistema","etiqueta":"A","x":0,"y":0}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0},{"id":"a","tipo":"sistema","etiqueta":"B","x":0,"y":0}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"raro","etiqueta":"A","x":0,"y":0}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"  ","x":0,"y":0}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":9999999,"y":0}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0,"elementoLikeC4":"<x>"}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0,"aplicativoId":"00000000-0000-0000-0000-000000000001"}],"relaciones":[]}""")]
    [InlineData("""{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A","x":0,"y":0}],"relaciones":[{"id":"r","origen":"a","destino":"z"}]}""")]
    public void Un_lienzo_invalido_se_rechaza(string json) => Assert.Null(ValidadorLienzo.Validar(json, Catalogo).Fuente);

    [Theory]
    [InlineData("flujo-de-radicacion", true)]
    [InlineData("a1", true)]
    [InlineData("-a", false)]
    [InlineData("A", false)]
    [InlineData("a b", false)]
    [InlineData("../x", false)]
    public void El_codigo_va_en_minusculas_numeros_y_guiones(string codigo, bool valido) => Assert.Equal(valido, ReglasDiagramas.CodigoValido(codigo));
}
