// Package diferencias calcula el diff entre dos versiones de un contrato (HU 3.5, ATLAS-ADR-0010 y ATLAS-ADR-0036).
//
// Para OpenAPI usa oasdiff como librería con la misma configuración que la CLI por defecto (`oasdiff breaking` y
// `oasdiff changelog`), salvo que no resuelve referencias externas: las specs llegan de fuera y una `$ref` a una URL
// sería una puerta a SSRF. AsyncAPI y proto todavía no tienen un motor maduro en Go: se rechazan con
// ErrTipoNoSoportado.
package diferencias

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"net/url"
	"runtime/debug"
	"strings"

	"github.com/getkin/kin-openapi/openapi3"
	"github.com/oasdiff/oasdiff/checker"
	"github.com/oasdiff/oasdiff/diff"
	"github.com/oasdiff/oasdiff/formatters"
	"github.com/oasdiff/oasdiff/load"
)

// Tipos de contrato (los mismos de la publicación, plan 00 §8.4).
const (
	TipoOpenAPI  = "openapi"
	TipoAsyncAPI = "asyncapi"
	TipoProto    = "proto"
)

// Idiomas del texto de los cambios. oasdiff trae traducción al español; lo que no esté traducido sale en inglés.
const (
	IdiomaEspanol = "es"
	IdiomaIngles  = "en"
)

// Lados del diff, para los errores.
const (
	LadoBase     = "base"
	LadoRevision = "revision"
)

// ErrTipoNoSoportado: el tipo es válido pero todavía no tiene motor de diff (AsyncAPI y proto, pendiente).
var ErrTipoNoSoportado = errors.New("diff no soportado para este tipo")

// ErrTipoDesconocido: el tipo no es openapi, asyncapi ni proto.
var ErrTipoDesconocido = errors.New("tipo de contrato desconocido")

// ErrIdiomaDesconocido: el idioma no es es ni en.
var ErrIdiomaDesconocido = errors.New("idioma no soportado")

// SpecInvalidaError: una de las dos specs no se pudo cargar como OpenAPI.
type SpecInvalidaError struct {
	Lado  string
	Causa error
}

func (e *SpecInvalidaError) Error() string {
	return fmt.Sprintf("la spec %s no es un OpenAPI válido: %v", e.Lado, e.Causa)
}

func (e *SpecInvalidaError) Unwrap() error { return e.Causa }

// Fuente es la ubicación de un cambio dentro de la spec (base o revisión). Sin nombre de archivo: las specs llegan
// en memoria.
type Fuente struct {
	Linea   int `json:"linea"`
	Columna int `json:"columna"`
}

// Cambio es un cambio detectado por oasdiff.
type Cambio struct {
	// Regla es el id de la regla de oasdiff (p. ej. api-removed-without-deprecation).
	Regla string `json:"regla"`
	// Nivel es error, warning o info. Error y warning son cambios incompatibles (breaking).
	Nivel       string   `json:"nivel"`
	Breaking    bool     `json:"breaking"`
	Texto       string   `json:"texto"`
	Comentario  string   `json:"comentario,omitempty"`
	Metodo      string   `json:"metodo,omitempty"`
	Ruta        string   `json:"ruta,omitempty"`
	OperationID string   `json:"operationId,omitempty"`
	Seccion     string   `json:"seccion,omitempty"`
	Avisos      []string `json:"avisos,omitempty"`
	// Huella es la de oasdiff: estable entre ejecuciones y no depende de la ubicación en el archivo.
	Huella         string  `json:"huella"`
	FuenteBase     *Fuente `json:"fuenteBase,omitempty"`
	FuenteRevision *Fuente `json:"fuenteRevision,omitempty"`
}

// Resumen cuenta los cambios por nivel.
type Resumen struct {
	Total        int  `json:"total"`
	Errores      int  `json:"errores"`
	Advertencias int  `json:"advertencias"`
	Informativos int  `json:"informativos"`
	Breaking     int  `json:"breaking"`
	HayBreaking  bool `json:"hayBreaking"`
}

// Resultado es la respuesta del diff.
type Resultado struct {
	Tipo  string `json:"tipo"`
	Motor string `json:"motor"`
	// Idioma del texto de los cambios y del changelog.
	Idioma          string `json:"idioma"`
	HashBase        string `json:"hashBase"`
	HashRevision    string `json:"hashRevision"`
	VersionBase     string `json:"versionBase"`
	VersionRevision string `json:"versionRevision"`
	// SinDiferencias es verdadero cuando oasdiff no encontró ninguna diferencia estructural.
	SinDiferencias bool    `json:"sinDiferencias"`
	Resumen        Resumen `json:"resumen"`
	// Cambios es el changelog completo (nivel info y superiores), como `oasdiff changelog`.
	Cambios []Cambio `json:"cambios"`
	// Breaking son los cambios incompatibles (error y warning), como `oasdiff breaking`.
	Breaking []Cambio `json:"breaking"`
	// Changelog es el changelog legible en Markdown.
	Changelog string `json:"changelog"`
}

// Hash devuelve el SHA-256 en hexadecimal de una spec, tal como llega (sin normalizar).
func Hash(spec []byte) string {
	suma := sha256.Sum256(spec)
	return hex.EncodeToString(suma[:])
}

// VersionOasdiff es la versión del módulo de oasdiff enlazado en el binario (la de go.mod).
func VersionOasdiff() string {
	if info, ok := debug.ReadBuildInfo(); ok {
		for _, dep := range info.Deps {
			if dep.Path == "github.com/oasdiff/oasdiff" {
				if dep.Replace != nil {
					return dep.Replace.Version
				}
				return dep.Version
			}
		}
	}
	return "desconocida"
}

// ValidarTipoEIdioma normaliza y valida el tipo y el idioma; el idioma vacío es español.
func ValidarTipoEIdioma(tipo, idioma string) (string, string, error) {
	tipo = strings.ToLower(strings.TrimSpace(tipo))
	idioma = strings.ToLower(strings.TrimSpace(idioma))
	if idioma == "" {
		idioma = IdiomaEspanol
	}
	switch tipo {
	case TipoOpenAPI:
	case TipoAsyncAPI, TipoProto:
		return tipo, idioma, ErrTipoNoSoportado
	default:
		return tipo, idioma, ErrTipoDesconocido
	}
	if idioma != IdiomaEspanol && idioma != IdiomaIngles {
		return tipo, idioma, ErrIdiomaDesconocido
	}
	return tipo, idioma, nil
}

// CalcularOpenAPI compara dos specs OpenAPI (YAML o JSON) y devuelve el changelog y los breaking changes.
// Un pánico de la librería ante una spec rara se convierte en error: nunca tumba el proceso.
func CalcularOpenAPI(base, revision []byte, idioma string) (resultado *Resultado, err error) {
	defer func() {
		if r := recover(); r != nil {
			resultado, err = nil, fmt.Errorf("oasdiff falló al comparar las specs: %v", r)
		}
	}()

	specBase, err := cargar(base, LadoBase)
	if err != nil {
		return nil, err
	}
	specRevision, err := cargar(revision, LadoRevision)
	if err != nil {
		return nil, err
	}

	cambios, diffVacio, err := cambiosOpenAPI(specBase, specRevision)
	if err != nil {
		return nil, err
	}

	localizador := checker.NewLocalizer(idioma)
	todos := convertir(formatters.NewChanges(cambios, localizador))
	breaking := make([]Cambio, 0, len(todos))
	for _, c := range todos {
		if c.Breaking {
			breaking = append(breaking, c)
		}
	}

	pareja := load.NewSpecInfoPair(specBase, specRevision)
	res := &Resultado{
		Tipo:            TipoOpenAPI,
		Motor:           "oasdiff " + VersionOasdiff(),
		Idioma:          idioma,
		HashBase:        Hash(base),
		HashRevision:    Hash(revision),
		VersionBase:     pareja.GetBaseVersion(),
		VersionRevision: pareja.GetRevisionVersion(),
		SinDiferencias:  diffVacio,
		Resumen:         resumir(todos),
		Cambios:         todos,
		Breaking:        breaking,
	}
	res.Changelog = Changelog(res)
	return res, nil
}

// cargar lee una spec en memoria con la configuración de la CLI (IncludeOrigin para ubicar los cambios y encabezados
// sin distinguir mayúsculas, que es el valor por defecto de --case-insensitive-headers), pero sin referencias externas
// (equivale a --allow-external-refs=false). Cada spec usa su propio cargador para que la caché de documentos del
// cargador no mezcle las dos.
func cargar(spec []byte, lado string) (*load.SpecInfo, error) {
	cargador := openapi3.NewLoader()
	cargador.IncludeOrigin = true
	cargador.IsExternalRefsAllowed = false
	cargador.ReadFromURIFunc = func(_ *openapi3.Loader, u *url.URL) ([]byte, error) {
		return nil, fmt.Errorf("referencia externa no permitida: %s", u.Redacted())
	}
	info, err := load.NewSpecInfoFromData(cargador, spec, lado+".yaml", load.WithLowercaseHeaders())
	if err != nil {
		return nil, &SpecInvalidaError{Lado: lado, Causa: err}
	}
	if info.Spec == nil || info.Spec.OpenAPI == "" {
		return nil, &SpecInvalidaError{Lado: lado, Causa: errors.New("falta el campo openapi")}
	}
	return info, nil
}

// cambiosOpenAPI reproduce el flujo de la CLI (internal/changelog.go de oasdiff): diff con la configuración por
// defecto y todas las reglas con sus niveles por defecto, desde el nivel info.
func cambiosOpenAPI(base, revision *load.SpecInfo) (checker.Changes, bool, error) {
	reporte, fuentes, err := diff.GetWithOperationsSourcesMap(diff.NewConfig(), base, revision)
	if err != nil {
		return nil, false, fmt.Errorf("oasdiff no pudo calcular el diff: %w", err)
	}
	configuracion := checker.NewConfig(
		checker.GetAllChecks(),
		checker.WithDeprecation(checker.DefaultBetaDeprecationDays, checker.DefaultStableDeprecationDays),
	)
	return checker.CheckBackwardCompatibilityUntilLevel(configuracion, reporte, fuentes, checker.INFO), reporte.Empty(), nil
}

func convertir(cambios formatters.Changes) []Cambio {
	resultado := make([]Cambio, 0, len(cambios))
	for _, c := range cambios {
		resultado = append(resultado, Cambio{
			Regla:          c.Id,
			Nivel:          c.Level.String(),
			Breaking:       c.Level.IsBreaking(),
			Texto:          c.Text,
			Comentario:     c.Comment,
			Metodo:         c.Operation,
			Ruta:           c.Path,
			OperationID:    c.OperationId,
			Seccion:        c.Section,
			Avisos:         c.Disclaimers,
			Huella:         c.Fingerprint,
			FuenteBase:     fuente(c.BaseSource),
			FuenteRevision: fuente(c.RevisionSource),
		})
	}
	return resultado
}

func fuente(s *checker.Source) *Fuente {
	if s == nil || s.Line == 0 {
		return nil
	}
	return &Fuente{Linea: s.Line, Columna: s.Column}
}

func resumir(cambios []Cambio) Resumen {
	r := Resumen{Total: len(cambios)}
	for _, c := range cambios {
		switch c.Nivel {
		case checker.ERR.String():
			r.Errores++
		case checker.WARN.String():
			r.Advertencias++
		default:
			r.Informativos++
		}
	}
	r.Breaking = r.Errores + r.Advertencias
	r.HayBreaking = r.Breaking > 0
	return r
}
