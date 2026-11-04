package diferencias

// CA de la HU 3.5: el endpoint coincide con la CLI de oasdiff de la misma versión.
//
// La prueba compila la CLI desde el módulo fijado en go.mod (directiva `tool`), así que la versión es la misma que la
// librería enlazada en atlas-render sin instalar nada en el CI. Para comparar con un binario publicado (por ejemplo, el
// de la release de GitHub que usa el job de contratos del CI), define OASDIFF_CLI con su ruta: la prueba exige que su
// --version coincida con la de go.mod. Si no hay CLI (sin toolchain de Go para compilarla, o
// ATLAS_RENDER_SIN_CLI=1), compara contra las mismas funciones públicas de checker que usa la CLI.

import (
	"bytes"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"runtime"
	"strings"
	"sync"
	"testing"

	"github.com/getkin/kin-openapi/openapi3"
	"github.com/oasdiff/oasdiff/checker"
	"github.com/oasdiff/oasdiff/diff"
	"github.com/oasdiff/oasdiff/formatters"
	"github.com/oasdiff/oasdiff/load"
)

const dirFixtures = "../../testdata/diff/openapi"

// casoCLI es un par de specs de prueba con lo que debe salir, además de coincidir con la CLI.
type casoCLI struct {
	revision      string
	reglasBreak   []string // reglas incompatibles esperadas (en cualquier orden, sin repetir)
	nivelMaximo   string   // nivel más alto entre los breaking ("" si no hay)
	conCambios    bool
	sinDiferencia bool
}

var casosCLI = []casoCLI{
	{revision: "base.yaml", sinDiferencia: true},
	{revision: "sin-breaking.yaml", conCambios: true},
	{revision: "renombre-campo.yaml", conCambios: true, reglasBreak: []string{"request-property-removed"}, nivelMaximo: "warning"},
	{revision: "campo-requerido-nuevo.yaml", conCambios: true, reglasBreak: []string{"new-required-request-property"}, nivelMaximo: "error"},
	{revision: "endpoint-eliminado.yaml", conCambios: true, reglasBreak: []string{"api-removed-without-deprecation"}, nivelMaximo: "error"},
	{revision: "cambio-tipo.yaml", conCambios: true, reglasBreak: []string{"request-property-type-changed", "response-property-type-changed"}, nivelMaximo: "error"},
}

// cambioComparable es lo que comparten la salida JSON de la CLI y la del endpoint. Se deja fuera el nombre del
// archivo de la ubicación: la CLI pone la ruta en disco y el endpoint no tiene archivo.
type cambioComparable struct {
	Regla, Nivel, Texto, Comentario, Metodo, Ruta, OperationID, Seccion, Huella string
	Avisos                                                                      []string
	LineaBase, ColumnaBase, LineaRevision, ColumnaRevision                      int
}

type fuenteCLI struct {
	Line   int `json:"line"`
	Column int `json:"column"`
}

type cambioCLI struct {
	ID             string     `json:"id"`
	Text           string     `json:"text"`
	Comment        string     `json:"comment"`
	Disclaimers    []string   `json:"disclaimers"`
	Level          int        `json:"level"`
	Operation      string     `json:"operation"`
	OperationID    string     `json:"operationId"`
	Path           string     `json:"path"`
	Section        string     `json:"section"`
	BaseSource     *fuenteCLI `json:"baseSource"`
	RevisionSource *fuenteCLI `json:"revisionSource"`
	Fingerprint    string     `json:"fingerprint"`
}

var nivelesCLI = map[int]string{1: "info", 2: "warning", 3: "error"}

func desdeCLI(cambios []cambioCLI) []cambioComparable {
	r := make([]cambioComparable, 0, len(cambios))
	for _, c := range cambios {
		x := cambioComparable{
			Regla: c.ID, Nivel: nivelesCLI[c.Level], Texto: c.Text, Comentario: c.Comment, Metodo: c.Operation,
			Ruta: c.Path, OperationID: c.OperationID, Seccion: c.Section, Huella: c.Fingerprint, Avisos: c.Disclaimers,
		}
		if c.BaseSource != nil {
			x.LineaBase, x.ColumnaBase = c.BaseSource.Line, c.BaseSource.Column
		}
		if c.RevisionSource != nil {
			x.LineaRevision, x.ColumnaRevision = c.RevisionSource.Line, c.RevisionSource.Column
		}
		r = append(r, x)
	}
	return r
}

func desdeEndpoint(cambios []Cambio) []cambioComparable {
	r := make([]cambioComparable, 0, len(cambios))
	for _, c := range cambios {
		x := cambioComparable{
			Regla: c.Regla, Nivel: c.Nivel, Texto: c.Texto, Comentario: c.Comentario, Metodo: c.Metodo,
			Ruta: c.Ruta, OperationID: c.OperationID, Seccion: c.Seccion, Huella: c.Huella, Avisos: c.Avisos,
		}
		if c.FuenteBase != nil {
			x.LineaBase, x.ColumnaBase = c.FuenteBase.Linea, c.FuenteBase.Columna
		}
		if c.FuenteRevision != nil {
			x.LineaRevision, x.ColumnaRevision = c.FuenteRevision.Linea, c.FuenteRevision.Columna
		}
		r = append(r, x)
	}
	return r
}

var (
	unaVezCLI sync.Once
	rutaCLI   string
	motivoCLI string
)

// cli devuelve la ruta de la CLI de oasdiff, o "" con el motivo si no hay.
func cli(t *testing.T) string {
	t.Helper()
	unaVezCLI.Do(func() {
		if os.Getenv("ATLAS_RENDER_SIN_CLI") == "1" {
			motivoCLI = "ATLAS_RENDER_SIN_CLI=1"
			return
		}
		if externa := os.Getenv("OASDIFF_CLI"); externa != "" {
			salida, err := exec.Command(externa, "--version").CombinedOutput()
			if err != nil {
				motivoCLI = fmt.Sprintf("OASDIFF_CLI no responde: %v", err)
				return
			}
			esperada, err := versionGoMod()
			if err != nil {
				motivoCLI = err.Error()
				return
			}
			esperada = strings.TrimPrefix(esperada, "v")
			if !strings.Contains(string(salida), esperada) {
				motivoCLI = fmt.Sprintf("OASDIFF_CLI es %q y go.mod fija %s", strings.TrimSpace(string(salida)), esperada)
				return
			}
			rutaCLI = externa
			return
		}
		goBin, err := exec.LookPath("go")
		if err != nil {
			motivoCLI = "no hay toolchain de Go para compilar la CLI"
			return
		}
		dir, err := os.MkdirTemp("", "atlas-render-oasdiff-")
		if err != nil {
			motivoCLI = err.Error()
			return
		}
		binario := filepath.Join(dir, "oasdiff")
		if runtime.GOOS == "windows" {
			binario += ".exe"
		}
		// Compila el paquete main de oasdiff con las versiones exactas de go.mod/go.sum (sin red si ya están en caché).
		comando := exec.Command(goBin, "build", "-o", binario, "github.com/oasdiff/oasdiff")
		if salida, err := comando.CombinedOutput(); err != nil {
			motivoCLI = fmt.Sprintf("no se pudo compilar la CLI: %v\n%s", err, salida)
			return
		}
		rutaCLI = binario
	})
	if rutaCLI == "" {
		t.Logf("CLI de oasdiff no disponible (%s): se compara contra la API pública de checker", motivoCLI)
	}
	return rutaCLI
}

// versionGoMod lee la versión de oasdiff fijada en go.mod. En los binarios de prueba debug.ReadBuildInfo no trae las
// dependencias, así que VersionOasdiff devuelve «desconocida» y no sirve para comparar.
func versionGoMod() (string, error) {
	datos, err := os.ReadFile("../../go.mod")
	if err != nil {
		return "", err
	}
	for linea := range strings.Lines(string(datos)) {
		campos := strings.Fields(linea)
		if len(campos) >= 2 && campos[0] == "github.com/oasdiff/oasdiff" {
			return campos[1], nil
		}
	}
	return "", errors.New("go.mod no fija github.com/oasdiff/oasdiff")
}

// ejecutarCLI corre `oasdiff <comando>` con los flags equivalentes a los del endpoint y devuelve los cambios.
func ejecutarCLI(t *testing.T, binario, comando, base, revision, idioma string) []cambioComparable {
	t.Helper()
	cmd := exec.Command(binario, comando, base, revision, "--format", "json", "--lang", idioma,
		"--allow-external-refs=false")
	var salida, errores bytes.Buffer
	cmd.Stdout, cmd.Stderr = &salida, &errores
	if err := cmd.Run(); err != nil {
		t.Fatalf("oasdiff %s falló: %v\n%s", comando, err, errores.String())
	}
	var cambios []cambioCLI
	if err := json.Unmarshal(salida.Bytes(), &cambios); err != nil {
		t.Fatalf("salida de oasdiff %s no es JSON: %v\n%s", comando, err, salida.String())
	}
	return desdeCLI(cambios)
}

// referenciaLibreria reproduce cada comando de la CLI con las funciones públicas de la librería, por separado y sin
// pasar por el motor de atlas-render: `breaking` usa el nivel warning y `changelog` el nivel info.
func referenciaLibreria(t *testing.T, comando, base, revision, idioma string) []cambioComparable {
	t.Helper()
	nivel := checker.INFO
	if comando == "breaking" {
		nivel = checker.WARN
	}
	cargarSpec := func(ruta string) *load.SpecInfo {
		datos, err := os.ReadFile(ruta)
		if err != nil {
			t.Fatal(err)
		}
		cargador := openapi3.NewLoader()
		cargador.IncludeOrigin = true
		info, err := load.NewSpecInfoFromData(cargador, datos, ruta, load.WithLowercaseHeaders())
		if err != nil {
			t.Fatal(err)
		}
		return info
	}
	reporte, fuentes, err := diff.GetWithOperationsSourcesMap(diff.NewConfig(), cargarSpec(base), cargarSpec(revision))
	if err != nil {
		t.Fatal(err)
	}
	cambios := checker.CheckBackwardCompatibilityUntilLevel(checker.NewConfig(checker.GetAllChecks()), reporte, fuentes, nivel)
	salida, err := json.Marshal(formatters.NewChanges(cambios, checker.NewLocalizer(idioma)))
	if err != nil {
		t.Fatal(err)
	}
	var crudos []cambioCLI
	if err := json.Unmarshal(salida, &crudos); err != nil {
		t.Fatal(err)
	}
	return desdeCLI(crudos)
}

// pedirDiff llama al endpoint con dos archivos de fixtures.
func pedirDiff(t *testing.T, m http.Handler, base, revision, idioma string) Resultado {
	t.Helper()
	leer := func(ruta string) string {
		datos, err := os.ReadFile(ruta)
		if err != nil {
			t.Fatal(err)
		}
		return string(datos)
	}
	cuerpo, _ := json.Marshal(Solicitud{Tipo: TipoOpenAPI, Base: leer(base), Revision: leer(revision), Idioma: idioma})
	req := httptest.NewRequest(http.MethodPost, Ruta, bytes.NewReader(cuerpo))
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	m.ServeHTTP(rec, req)
	if rec.Code != http.StatusOK {
		t.Fatalf("código %d: %s", rec.Code, rec.Body.String())
	}
	var r Resultado
	if err := json.Unmarshal(rec.Body.Bytes(), &r); err != nil {
		t.Fatal(err)
	}
	return r
}

func TestEndpointCoincideConLaCLI(t *testing.T) {
	binario := cli(t)
	mux := http.NewServeMux()
	Registrar(mux, Opciones{MaxBytes: 1 << 20, Tiempo: defectoTiempoPruebas, Concurrencia: 2, Cache: NuevaCache(64, 1<<20)})
	base := filepath.Join(dirFixtures, "base.yaml")

	for _, caso := range casosCLI {
		for _, idioma := range []string{IdiomaEspanol, IdiomaIngles} {
			t.Run(strings.TrimSuffix(caso.revision, ".yaml")+"/"+idioma, func(t *testing.T) {
				revision := filepath.Join(dirFixtures, caso.revision)
				r := pedirDiff(t, mux, base, revision, idioma)

				for _, comando := range []string{"breaking", "changelog"} {
					obtenido := desdeEndpoint(r.Breaking)
					if comando == "changelog" {
						obtenido = desdeEndpoint(r.Cambios)
					}
					referencias := map[string][]cambioComparable{
						"librería": referenciaLibreria(t, comando, base, revision, idioma),
					}
					if binario != "" {
						referencias["CLI"] = ejecutarCLI(t, binario, comando, base, revision, idioma)
					}
					for origen, esperado := range referencias {
						if !reflect.DeepEqual(obtenido, esperado) {
							t.Errorf("oasdiff %s: el endpoint no coincide con la %s\nendpoint: %+v\n%s: %+v",
								comando, origen, obtenido, origen, esperado)
						}
					}
				}

				verificarCaso(t, caso, r)
			})
		}
	}
}

func verificarCaso(t *testing.T, caso casoCLI, r Resultado) {
	t.Helper()
	if r.SinDiferencias != caso.sinDiferencia {
		t.Errorf("sinDiferencias = %v", r.SinDiferencias)
	}
	if (len(r.Cambios) > 0) != caso.conCambios {
		t.Errorf("cambios = %d", len(r.Cambios))
	}
	reglas := map[string]bool{}
	maximo := ""
	orden := map[string]int{"": 0, "info": 1, "warning": 2, "error": 3}
	for _, c := range r.Breaking {
		if !c.Breaking || c.Regla == "" || (c.Nivel != "error" && c.Nivel != "warning") {
			t.Errorf("breaking mal formado: %+v", c)
		}
		reglas[c.Regla] = true
		if orden[c.Nivel] > orden[maximo] {
			maximo = c.Nivel
		}
	}
	esperadas := map[string]bool{}
	for _, regla := range caso.reglasBreak {
		esperadas[regla] = true
	}
	if !reflect.DeepEqual(reglas, esperadas) {
		t.Errorf("reglas breaking = %v, se esperaban %v", reglas, esperadas)
	}
	if maximo != caso.nivelMaximo {
		t.Errorf("nivel máximo = %q, se esperaba %q", maximo, caso.nivelMaximo)
	}
	if r.Resumen.Breaking != len(r.Breaking) || r.Resumen.Total != len(r.Cambios) || r.Resumen.HayBreaking != (len(r.Breaking) > 0) {
		t.Errorf("resumen incoherente: %+v", r.Resumen)
	}
	if r.Motor != "oasdiff "+VersionOasdiff() || r.VersionBase != "1.0.0" {
		t.Errorf("motor %q, versión base %q", r.Motor, r.VersionBase)
	}
}
