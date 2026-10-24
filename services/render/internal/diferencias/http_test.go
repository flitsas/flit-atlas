package diferencias

import (
	"bytes"
	"encoding/json"
	"errors"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/testutil"
)

const defectoTiempoPruebas = 30 * time.Second

func TestMain(m *testing.M) {
	codigo := m.Run()
	if rutaCLI != "" && os.Getenv("OASDIFF_CLI") == "" {
		_ = os.RemoveAll(filepath.Dir(rutaCLI))
	}
	os.Exit(codigo)
}

func fixture(t *testing.T, nombre string) string {
	t.Helper()
	datos, err := os.ReadFile(filepath.Join(dirFixtures, nombre))
	if err != nil {
		t.Fatal(err)
	}
	return string(datos)
}

func enviar(t *testing.T, h http.Handler, contentType string, cuerpo []byte) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodPost, Ruta, bytes.NewReader(cuerpo))
	if contentType != "" {
		req.Header.Set("Content-Type", contentType)
	}
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	return rec
}

func enviarSolicitud(t *testing.T, h http.Handler, sol Solicitud) *httptest.ResponseRecorder {
	t.Helper()
	cuerpo, err := json.Marshal(sol)
	if err != nil {
		t.Fatal(err)
	}
	return enviar(t, h, "application/json", cuerpo)
}

func leerProblema(t *testing.T, rec *httptest.ResponseRecorder) Problema {
	t.Helper()
	if ct := rec.Header().Get("Content-Type"); ct != tipoContenidoProblema {
		t.Fatalf("Content-Type = %q", ct)
	}
	var p Problema
	if err := json.Unmarshal(rec.Body.Bytes(), &p); err != nil {
		t.Fatal(err)
	}
	return p
}

func nuevoManejador(op Opciones) *Manejador {
	if op.MaxBytes == 0 {
		op.MaxBytes = 1 << 20
	}
	if op.Tiempo == 0 {
		op.Tiempo = defectoTiempoPruebas
	}
	if op.Cache == nil {
		op.Cache = NuevaCache(16, 1<<20)
	}
	return Nuevo(op)
}

func TestRechazaPeticionesInvalidas(t *testing.T) {
	m := nuevoManejador(Opciones{MaxBytes: 4096})
	base := fixture(t, "base.yaml")

	casos := []struct {
		nombre      string
		contentType string
		cuerpo      []byte
		estado      int
	}{
		{"sin json", "text/plain", []byte("{}"), http.StatusUnsupportedMediaType},
		{"json roto", "application/json", []byte("{"), http.StatusBadRequest},
		{"campo desconocido", "application/json", []byte(`{"tipo":"openapi","base":"a","revision":"b","otro":1}`), http.StatusBadRequest},
		{"tipo desconocido", "application/json", []byte(`{"tipo":"graphql","base":"a","revision":"b"}`), http.StatusBadRequest},
		{"idioma desconocido", "application/json", []byte(`{"tipo":"openapi","base":"a","revision":"b","idioma":"fr"}`), http.StatusBadRequest},
		{"sin revisión", "application/json", []byte(`{"tipo":"openapi","base":"a"}`), http.StatusBadRequest},
		{"demasiado grande", "application/json", []byte(`{"tipo":"openapi","base":` + strings.Repeat(" ", 4096) + `"a","revision":"b"}`), http.StatusRequestEntityTooLarge},
		{"demasiado grande con specs", "application/json", mustJSON(t, Solicitud{Tipo: TipoOpenAPI, Base: base, Revision: base + strings.Repeat("#", 4096)}), http.StatusRequestEntityTooLarge},
	}
	for _, c := range casos {
		t.Run(c.nombre, func(t *testing.T) {
			rec := enviar(t, m, c.contentType, c.cuerpo)
			if rec.Code != c.estado {
				t.Fatalf("código %d, se esperaba %d: %s", rec.Code, c.estado, rec.Body.String())
			}
			if p := leerProblema(t, rec); p.Estado != c.estado || p.Titulo == "" {
				t.Fatalf("problema = %+v", p)
			}
		})
	}
}

func mustJSON(t *testing.T, v any) []byte {
	t.Helper()
	b, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	return b
}

func TestAsyncAPIYProtoDevuelven422NoSoportado(t *testing.T) {
	m := nuevoManejador(Opciones{})
	for _, tipo := range []string{TipoAsyncAPI, TipoProto, "  AsyncAPI "} {
		rec := enviarSolicitud(t, m, Solicitud{Tipo: tipo, Base: "a", Revision: "b"})
		if rec.Code != http.StatusUnprocessableEntity {
			t.Fatalf("%s: código %d", tipo, rec.Code)
		}
		if p := leerProblema(t, rec); !strings.Contains(strings.ToLower(p.Titulo), "diff no soportado para este tipo") {
			t.Fatalf("%s: problema = %+v", tipo, p)
		}
	}
}

func TestSpecInvalidaDevuelve422ConElLado(t *testing.T) {
	m := nuevoManejador(Opciones{})
	base := fixture(t, "base.yaml")
	casos := map[string]Solicitud{
		LadoBase:     {Tipo: TipoOpenAPI, Base: "esto: [no es", Revision: base},
		LadoRevision: {Tipo: TipoOpenAPI, Base: base, Revision: `{"titulo": "sin campo openapi"}`},
	}
	for lado, sol := range casos {
		rec := enviarSolicitud(t, m, sol)
		if rec.Code != http.StatusUnprocessableEntity {
			t.Fatalf("%s: código %d: %s", lado, rec.Code, rec.Body.String())
		}
		if p := leerProblema(t, rec); p.Lado != lado {
			t.Fatalf("%s: problema = %+v", lado, p)
		}
	}
}

func TestNoResuelveReferenciasExternas(t *testing.T) {
	llamado := false
	servidor := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		llamado = true
		_, _ = w.Write([]byte("type: object\n"))
	}))
	defer servidor.Close()

	base := fixture(t, "base.yaml")
	revision := strings.Replace(base, `$ref: "#/components/schemas/NuevoTramite"`, `$ref: "`+servidor.URL+`/esquema.yaml"`, 1)
	rec := enviarSolicitud(t, nuevoManejador(Opciones{}), Solicitud{Tipo: TipoOpenAPI, Base: base, Revision: revision})
	if rec.Code != http.StatusUnprocessableEntity {
		t.Fatalf("código %d: %s", rec.Code, rec.Body.String())
	}
	if llamado {
		t.Fatal("atlas-render siguió una $ref externa (riesgo de SSRF)")
	}
}

func TestSegundaPeticionSaleDeLaCache(t *testing.T) {
	registro := prometheus.NewRegistry()
	calculos := 0
	m := nuevoManejador(Opciones{Registro: registro, Calcular: func(b, r []byte, idioma string) (*Resultado, error) {
		calculos++
		return CalcularOpenAPI(b, r, idioma)
	}})
	sol := Solicitud{Tipo: TipoOpenAPI, Base: fixture(t, "base.yaml"), Revision: fixture(t, "endpoint-eliminado.yaml")}

	primera := enviarSolicitud(t, m, sol)
	segunda := enviarSolicitud(t, m, sol)
	if primera.Code != http.StatusOK || segunda.Code != http.StatusOK {
		t.Fatalf("códigos %d y %d", primera.Code, segunda.Code)
	}
	if primera.Header().Get(encabezadoCache) != "fallo" || segunda.Header().Get(encabezadoCache) != "acierto" {
		t.Fatalf("caché: %q y %q", primera.Header().Get(encabezadoCache), segunda.Header().Get(encabezadoCache))
	}
	if !bytes.Equal(primera.Body.Bytes(), segunda.Body.Bytes()) || calculos != 1 {
		t.Fatalf("la segunda respuesta no salió de la caché (cálculos: %d)", calculos)
	}

	// Otro idioma u otro orden son otra clave.
	sol.Idioma = IdiomaIngles
	if rec := enviarSolicitud(t, m, sol); rec.Header().Get(encabezadoCache) != "fallo" {
		t.Fatal("el idioma debe ser parte de la clave")
	}
	sol.Base, sol.Revision = sol.Revision, sol.Base
	if rec := enviarSolicitud(t, m, sol); rec.Header().Get(encabezadoCache) != "fallo" {
		t.Fatal("el orden base → revisión debe ser parte de la clave")
	}

	if v := testutil.ToFloat64(m.metricas.peticiones.WithLabelValues(TipoOpenAPI, resultadoCache)); v != 1 {
		t.Fatalf("aciertos de caché = %v", v)
	}
	if v := testutil.ToFloat64(m.metricas.peticiones.WithLabelValues(TipoOpenAPI, resultadoOK)); v != 3 {
		t.Fatalf("cálculos = %v", v)
	}
	if n, err := testutil.GatherAndCount(registro, "atlas_render_diff_cache_entradas"); err != nil || n != 1 {
		t.Fatalf("métrica de caché: %d, %v", n, err)
	}
}

func TestTiempoAgotadoDevuelve504YElResultadoQuedaEnCache(t *testing.T) {
	soltar := make(chan struct{})
	terminado := make(chan struct{})
	m := nuevoManejador(Opciones{Tiempo: 50 * time.Millisecond, Calcular: func(b, r []byte, idioma string) (*Resultado, error) {
		defer close(terminado)
		<-soltar
		return &Resultado{Tipo: TipoOpenAPI}, nil
	}})
	sol := Solicitud{Tipo: TipoOpenAPI, Base: "a", Revision: "b"}

	if rec := enviarSolicitud(t, m, sol); rec.Code != http.StatusGatewayTimeout {
		t.Fatalf("código %d", rec.Code)
	}
	close(soltar)
	<-terminado
	// El cálculo terminó en segundo plano; el reintento sale de la caché.
	espera := time.Now().Add(2 * time.Second)
	for {
		rec := enviarSolicitud(t, m, sol)
		if rec.Code == http.StatusOK && rec.Header().Get(encabezadoCache) == "acierto" {
			break
		}
		if time.Now().After(espera) {
			t.Fatalf("el reintento no salió de la caché: %d %q", rec.Code, rec.Header().Get(encabezadoCache))
		}
		time.Sleep(10 * time.Millisecond)
	}
}

func TestSinTurnoLibreDevuelve503(t *testing.T) {
	soltar := make(chan struct{})
	ocupado := make(chan struct{})
	m := nuevoManejador(Opciones{Concurrencia: 1, Tiempo: 100 * time.Millisecond, Calcular: func(b, r []byte, idioma string) (*Resultado, error) {
		close(ocupado)
		<-soltar
		return nil, errors.New("fin")
	}})
	primera := mustJSON(t, Solicitud{Tipo: TipoOpenAPI, Base: "a", Revision: "b"})
	go func() {
		req := httptest.NewRequest(http.MethodPost, Ruta, bytes.NewReader(primera))
		req.Header.Set("Content-Type", "application/json")
		m.ServeHTTP(httptest.NewRecorder(), req)
	}()
	<-ocupado
	rec := enviarSolicitud(t, m, Solicitud{Tipo: TipoOpenAPI, Base: "c", Revision: "d"})
	close(soltar)
	if rec.Code != http.StatusServiceUnavailable || rec.Header().Get("Retry-After") == "" {
		t.Fatalf("código %d", rec.Code)
	}
}

func TestErrorDelMotorNoFiltraDetalles(t *testing.T) {
	m := nuevoManejador(Opciones{Calcular: func(b, r []byte, idioma string) (*Resultado, error) {
		return nil, errors.New("detalle con contenido de la spec")
	}})
	rec := enviarSolicitud(t, m, Solicitud{Tipo: TipoOpenAPI, Base: "a", Revision: "b"})
	if rec.Code != http.StatusInternalServerError || strings.Contains(rec.Body.String(), "contenido de la spec") {
		t.Fatalf("código %d: %s", rec.Code, rec.Body.String())
	}
}

func TestLosLogsNoLlevanContenidoDeLasSpecs(t *testing.T) {
	var logs bytes.Buffer
	m := nuevoManejador(Opciones{Log: slog.New(slog.NewJSONHandler(&logs, nil))})
	const marca = "MarcaSecretaDeLaSpec7Q"
	base := fixture(t, "base.yaml")
	revision := strings.Replace(fixture(t, "endpoint-eliminado.yaml"), "Obtiene un trámite", marca, 1)
	invalida := "openapi: 3.0.3\ninfo: [" + marca

	for _, sol := range []Solicitud{
		{Tipo: TipoOpenAPI, Base: base, Revision: revision},
		{Tipo: TipoOpenAPI, Base: base, Revision: invalida},
	} {
		enviarSolicitud(t, m, sol)
	}
	if strings.Contains(logs.String(), marca) || strings.Contains(logs.String(), "/tramites") {
		t.Fatalf("los logs llevan contenido de las specs:\n%s", logs.String())
	}
	if !strings.Contains(logs.String(), `"hash_base"`) || !strings.Contains(logs.String(), `"resultado":"spec_invalida"`) {
		t.Fatalf("faltan campos esperados en los logs:\n%s", logs.String())
	}
}

func TestChangelogEnEspanol(t *testing.T) {
	r, err := CalcularOpenAPI([]byte(fixture(t, "base.yaml")), []byte(fixture(t, "endpoint-eliminado.yaml")), IdiomaEspanol)
	if err != nil {
		t.Fatal(err)
	}
	for _, esperado := range []string{"# Cambios del contrato 1.0.0 → 2.0.0", "## DELETE /tramites/{id}", "**error** api removida sin deprecación (`api-removed-without-deprecation`)"} {
		if !strings.Contains(r.Changelog, esperado) {
			t.Errorf("el changelog no contiene %q:\n%s", esperado, r.Changelog)
		}
	}

	sin, err := CalcularOpenAPI([]byte(fixture(t, "base.yaml")), []byte(fixture(t, "base.yaml")), IdiomaEspanol)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(sin.Changelog, "Sin cambios.") {
		t.Errorf("changelog sin cambios:\n%s", sin.Changelog)
	}
}
