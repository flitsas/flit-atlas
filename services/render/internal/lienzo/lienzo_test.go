package lienzo

import (
	"context"
	"encoding/xml"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync/atomic"
	"testing"
	"time"
)

func modeloDemo() Modelo {
	return Modelo{
		Nodos: []Nodo{
			{ID: "a", Tipo: "actor", Etiqueta: "Radicador"},
			{ID: "b", Tipo: "sistema", Etiqueta: "Trámites"},
			{ID: "c", Tipo: "nota", Etiqueta: "Revisar con negocio"},
		},
		Relaciones: []Relacion{{Origen: "a", Destino: "b", Etiqueta: "radica"}, {Origen: "b", Destino: "c"}},
	}
}

func TestRenderizaUnSvgLimpioConLasEtiquetas(t *testing.T) {
	svg, err := Renderizar(context.Background(), modeloDemo())
	if err != nil {
		t.Fatal(err)
	}
	texto := string(svg)
	for _, esperado := range []string{"<svg", "Radicador", "Trámites", "radica", "@font-face"} {
		if !strings.Contains(texto, esperado) {
			t.Errorf("falta %q en el SVG", esperado)
		}
	}
	revisarSinPeligro(t, texto)
}

// Una etiqueta no puede cambiar la estructura del D2 ni llegar como marcado.
func TestLasEtiquetasMaliciosasSonSoloTexto(t *testing.T) {
	maliciosas := []string{
		`<script>alert(1)</script>`,
		`' {link: https://malo.example.com}`,
		"uno\nn9: 'otro' {link: https://malo.example.com}",
		`${variable} ...@importado`,
		`<img src=x onerror=alert(1)>`,
		`[x](javascript:alert(1))`,
		`"></text><script>alert(1)</script>`,
	}
	for _, etiqueta := range maliciosas {
		m := Modelo{
			Nodos:      []Nodo{{ID: "a", Tipo: "sistema", Etiqueta: etiqueta}, {ID: "b", Tipo: "sistema", Etiqueta: "b"}},
			Relaciones: []Relacion{{Origen: "a", Destino: "b", Etiqueta: etiqueta}},
		}
		svg, err := Renderizar(context.Background(), m)
		if err != nil {
			t.Fatalf("%q: %v", etiqueta, err)
		}
		revisarSinPeligro(t, string(svg))
	}
}

// revisarSinPeligro lee el SVG como XML, porque una etiqueta puede decir «onerror=» como texto: eso es inofensivo.
func revisarSinPeligro(t *testing.T, svg string) {
	t.Helper()
	lector := xml.NewDecoder(strings.NewReader(svg))
	for {
		token, err := lector.RawToken()
		if errors.Is(err, io.EOF) {
			return
		}
		if err != nil {
			t.Fatalf("el SVG no es XML válido: %v", err)
		}
		inicio, ok := token.(xml.StartElement)
		if !ok {
			continue
		}
		if !elementosPermitidos[inicio.Name.Local] {
			t.Errorf("el SVG trae el elemento %q", inicio.Name.Local)
		}
		for _, a := range inicio.Attr {
			nombre := strings.ToLower(a.Name.Local)
			if strings.HasPrefix(nombre, "on") || ((nombre == "href" || nombre == "src") && !strings.HasPrefix(a.Value, "#")) {
				t.Errorf("el SVG trae el atributo %s=%q", nombre, a.Value)
			}
		}
	}
}

func TestSanearQuitaLoPeligroso(t *testing.T) {
	entrada := `<?xml version="1.0"?><!DOCTYPE svg><svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xmlns:x="http://malo.example.com" onload="a()">` +
		`<!-- c --><script>alert(1)</script><SCRIPT>alert(2)</SCRIPT><foreignObject><div>x</div></foreignObject><a href="https://malo.example.com"><text>dentro</text></a>` +
		`<image href="https://malo.example.com/x.png"/><use xlink:href="https://malo.example.com/x.svg#a"/><x:cosa/>` +
		`<rect onclick="a()" ONMOUSEOVER="b()" fill="url(https://malo.example.com/f)" mask="url(#m)" width="1"/>` +
		`<text style="fill:url(//malo.example.com/f)" class="c">bien &lt;b&gt;</text></svg>`
	salida, err := Sanear([]byte(entrada))
	if err != nil {
		t.Fatal(err)
	}
	texto := string(salida)
	for _, prohibido := range []string{"script", "alert", "foreignObject", "<a", "dentro", "<image", "<use", "malo.example.com", "onclick", "ONMOUSEOVER", "onload", "<!", "<?", "x:cosa", "xmlns:x="} {
		if strings.Contains(texto, prohibido) {
			t.Errorf("sigue %q en %s", prohibido, texto)
		}
	}
	for _, esperado := range []string{`mask="url(#m)"`, `class="c"`, "bien &lt;b&gt;", `xmlns="http://www.w3.org/2000/svg"`} {
		if !strings.Contains(texto, esperado) {
			t.Errorf("falta %q en %s", esperado, texto)
		}
	}
}

func TestSanearRechazaCssPeligroso(t *testing.T) {
	for _, css := range []string{
		`@import url(https://malo.example.com/a.css);`,
		`a{background:url(https://malo.example.com/x)}`,
		`a{background:\75rl(https://malo.example.com/x)}`,
		`@font-face{src:url("data:text/html;base64,AAAA")}`,
	} {
		_, err := Sanear([]byte(`<svg xmlns="http://www.w3.org/2000/svg"><style>` + css + `</style></svg>`))
		if !errors.Is(err, ErrSVGInseguro) {
			t.Errorf("%q: se esperaba ErrSVGInseguro, llegó %v", css, err)
		}
	}
	if _, err := Sanear([]byte(`<svg xmlns="http://www.w3.org/2000/svg"><style>@font-face{src:url("data:application/font-woff;base64,AAAA")}</style></svg>`)); err != nil {
		t.Errorf("una fuente incrustada debe pasar: %v", err)
	}
	if _, err := Sanear([]byte(`<svg><rect`)); err == nil {
		t.Error("un XML cortado debe fallar")
	}
}

func TestGenerarD2Valida(t *testing.T) {
	casos := map[string]Modelo{
		"sin nodos":  {},
		"tipo":       {Nodos: []Nodo{{ID: "a", Tipo: "otro", Etiqueta: "x"}}},
		"sin texto":  {Nodos: []Nodo{{ID: "a", Tipo: "nota", Etiqueta: "  \n "}}},
		"repetido":   {Nodos: []Nodo{{ID: "a", Tipo: "nota", Etiqueta: "x"}, {ID: "a", Tipo: "nota", Etiqueta: "y"}}},
		"suelta":     {Nodos: []Nodo{{ID: "a", Tipo: "nota", Etiqueta: "x"}}, Relaciones: []Relacion{{Origen: "a", Destino: "z"}}},
		"demasiados": {Nodos: make([]Nodo, MaximoNodos+1)},
	}
	for nombre, m := range casos {
		if _, err := GenerarD2(m); !errors.Is(err, ErrModeloInvalido) {
			t.Errorf("%s: %v", nombre, err)
		}
	}
	d2, err := GenerarD2(Modelo{Nodos: []Nodo{{ID: "id raro", Tipo: "sistema", Etiqueta: "O'Neil\nDos"}}})
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(d2, "n0: 'O''Neil Dos' {shape: rectangle}") || strings.Contains(d2, "id raro") {
		t.Errorf("D2 inesperado: %s", d2)
	}
}

func pedir(h http.Handler, cuerpo, tipo string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(http.MethodPost, Ruta, strings.NewReader(cuerpo))
	r.Header.Set("Content-Type", tipo)
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	return w
}

const cuerpoDemo = `{"nodos":[{"id":"a","tipo":"sistema","etiqueta":"A"}],"relaciones":[]}`

func TestHttpResponderYCache(t *testing.T) {
	var llamadas atomic.Int32
	h := Nuevo(Opciones{Renderizar: func(context.Context, Modelo) ([]byte, error) {
		llamadas.Add(1)
		return []byte("<svg></svg>"), nil
	}})
	primera := pedir(h, cuerpoDemo, "application/json")
	if primera.Code != http.StatusOK || primera.Header().Get(encabezadoCache) != "fallo" || primera.Header().Get("Content-Type") != tipoSVG {
		t.Fatalf("%d %v", primera.Code, primera.Header())
	}
	// El mismo modelo con otros espacios sale de la caché.
	segunda := pedir(h, ` {"nodos": [{"etiqueta":"A","id":"a","tipo":"sistema"}], "relaciones": [] }`, "application/json")
	if segunda.Header().Get(encabezadoCache) != "acierto" || llamadas.Load() != 1 {
		t.Fatalf("caché: %v, llamadas %d", segunda.Header(), llamadas.Load())
	}
}

func TestHttpRechazos(t *testing.T) {
	h := Nuevo(Opciones{MaxBytes: 200, Renderizar: func(context.Context, Modelo) ([]byte, error) { return []byte("<svg></svg>"), nil }})
	casos := []struct {
		cuerpo, tipo string
		estado       int
	}{
		{cuerpoDemo, "text/plain", http.StatusUnsupportedMediaType},
		{"{", "application/json", http.StatusBadRequest},
		{`{"nodos":[],"otro":1}`, "application/json", http.StatusBadRequest},
		{`{"nodos":[],"relaciones":[]}`, "application/json", http.StatusUnprocessableEntity},
		{`{"nodos":[{"id":"` + strings.Repeat("a", 300) + `"}]}`, "application/json", http.StatusRequestEntityTooLarge},
	}
	for _, c := range casos {
		if w := pedir(h, c.cuerpo, c.tipo); w.Code != c.estado {
			t.Errorf("%q: %d, se esperaba %d", c.cuerpo, w.Code, c.estado)
		}
	}
}

func TestHttpUnPanicNoTumbaYElErrorNoCitaContenido(t *testing.T) {
	h := Nuevo(Opciones{Renderizar: func(context.Context, Modelo) ([]byte, error) { panic("secreto") }})
	w := pedir(h, cuerpoDemo, "application/json")
	if w.Code != http.StatusInternalServerError || strings.Contains(w.Body.String(), "secreto") {
		t.Fatalf("%d %s", w.Code, w.Body)
	}
	// El turno se liberó.
	if w := pedir(h, cuerpoDemo, "application/json"); w.Code != http.StatusInternalServerError {
		t.Fatalf("%d", w.Code)
	}
}

func TestHttpTiempoAgotadoDejaElResultadoEnCache(t *testing.T) {
	libera := make(chan struct{})
	termino := make(chan struct{})
	h := Nuevo(Opciones{Tiempo: 20 * time.Millisecond, Renderizar: func(context.Context, Modelo) ([]byte, error) {
		defer close(termino)
		<-libera
		return []byte("<svg></svg>"), nil
	}})
	if w := pedir(h, cuerpoDemo, "application/json"); w.Code != http.StatusGatewayTimeout {
		t.Fatalf("%d", w.Code)
	}
	close(libera)
	<-termino
	time.Sleep(20 * time.Millisecond)
	if w := pedir(h, cuerpoDemo, "application/json"); w.Code != http.StatusOK || w.Header().Get(encabezadoCache) != "acierto" {
		t.Fatalf("%d %v", w.Code, w.Header())
	}
}
