package diferencias

import (
	"context"
	"encoding/json"
	"errors"
	"log/slog"
	"mime"
	"net/http"
	"strconv"
	"time"

	"github.com/prometheus/client_golang/prometheus"
)

// Ruta del endpoint interno. No se expone a internet: lo llama atlas-api (IRenderDiff) por la red del clúster.
const Ruta = "/internal/v1/contratos/diff"

// Resultados para las métricas y los logs.
const (
	resultadoOK             = "ok"
	resultadoCache          = "cache"
	resultadoSolicitud      = "solicitud_invalida"
	resultadoGrande         = "demasiado_grande"
	resultadoNoSoportado    = "no_soportado"
	resultadoSpecInvalida   = "spec_invalida"
	resultadoOcupado        = "ocupado"
	resultadoTiempoAgotado  = "tiempo_agotado"
	resultadoErrorInterno   = "error"
	encabezadoCache         = "X-Atlas-Cache"
	maxDetalleError         = 300
	tipoContenidoProblema   = "application/problem+json"
	tipoContenidoRespuesta  = "application/json; charset=utf-8"
	tipoContenidoEsperado   = "application/json"
	longitudPrefijoHashLogs = 12
)

// Solicitud es el cuerpo del POST: las dos specs completas como texto (YAML o JSON).
type Solicitud struct {
	Tipo     string `json:"tipo"`
	Base     string `json:"base"`
	Revision string `json:"revision"`
	// Idioma del texto de los cambios: es (por defecto) o en.
	Idioma string `json:"idioma,omitempty"`
}

// Problema es la respuesta de error (RFC 9457). Nunca lleva contenido de las specs, salvo el mensaje del analizador
// recortado en Detalle cuando la spec es inválida.
type Problema struct {
	Titulo  string `json:"title"`
	Estado  int    `json:"status"`
	Detalle string `json:"detail,omitempty"`
	// Lado dice qué spec falló (base o revision) cuando el problema es una spec inválida.
	Lado string `json:"lado,omitempty"`
}

// Opciones del manejador.
type Opciones struct {
	MaxBytes     int64
	Tiempo       time.Duration
	Concurrencia int
	Cache        *Cache
	Log          *slog.Logger
	Registro     prometheus.Registerer
	// Calcular permite sustituir el motor en las pruebas; por defecto, CalcularOpenAPI.
	Calcular func(base, revision []byte, idioma string) (*Resultado, error)
}

// Manejador atiende POST /internal/v1/contratos/diff.
type Manejador struct {
	op       Opciones
	turnos   chan struct{}
	metricas metricas
	motor    string
}

type metricas struct {
	peticiones *prometheus.CounterVec
	duracion   *prometheus.HistogramVec
	enCurso    prometheus.Gauge
}

// Registrar crea el manejador, registra sus métricas y la ruta en el mux.
func Registrar(mux *http.ServeMux, op Opciones) *Manejador {
	m := Nuevo(op)
	mux.Handle("POST "+Ruta, m)
	return m
}

// Nuevo crea el manejador y registra sus métricas en op.Registro (si hay).
func Nuevo(op Opciones) *Manejador {
	if op.Concurrencia < 1 {
		op.Concurrencia = 1
	}
	if op.Cache == nil {
		op.Cache = NuevaCache(0, 0)
	}
	if op.Log == nil {
		op.Log = slog.New(slog.DiscardHandler)
	}
	if op.Calcular == nil {
		op.Calcular = CalcularOpenAPI
	}
	m := &Manejador{op: op, turnos: make(chan struct{}, op.Concurrencia), motor: "oasdiff " + VersionOasdiff()}
	m.metricas = metricas{
		peticiones: prometheus.NewCounterVec(prometheus.CounterOpts{
			Name: "atlas_render_diff_peticiones_total",
			Help: "Peticiones de diff de contratos por tipo y resultado.",
		}, []string{"tipo", "resultado"}),
		duracion: prometheus.NewHistogramVec(prometheus.HistogramOpts{
			Name:    "atlas_render_diff_duracion_segundos",
			Help:    "Duración de las peticiones de diff respondidas con éxito, con y sin acierto de caché.",
			Buckets: []float64{.005, .01, .025, .05, .1, .25, .5, 1, 2.5, 5, 10, 30},
		}, []string{"cache"}),
		enCurso: prometheus.NewGauge(prometheus.GaugeOpts{
			Name: "atlas_render_diff_en_curso",
			Help: "Diffs que se están calculando en este momento.",
		}),
	}
	if op.Registro != nil {
		op.Registro.MustRegister(m.metricas.peticiones, m.metricas.duracion, m.metricas.enCurso,
			prometheus.NewGaugeFunc(prometheus.GaugeOpts{
				Name: "atlas_render_diff_cache_entradas",
				Help: "Entradas en la caché LRU de diffs.",
			}, func() float64 { n, _ := op.Cache.Tamano(); return float64(n) }),
			prometheus.NewGaugeFunc(prometheus.GaugeOpts{
				Name: "atlas_render_diff_cache_bytes",
				Help: "Bytes ocupados por la caché LRU de diffs.",
			}, func() float64 { _, b := op.Cache.Tamano(); return float64(b) }),
		)
	}
	return m
}

type salida struct {
	cuerpo    []byte
	resultado *Resultado
	err       error
}

func (m *Manejador) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	inicio := time.Now()
	tipoMetrica := "desconocido"
	registrar := func(resultado string, atributos ...any) {
		m.metricas.peticiones.WithLabelValues(tipoMetrica, resultado).Inc()
		atributos = append(atributos, "resultado", resultado, "tipo", tipoMetrica,
			"duracion_ms", time.Since(inicio).Milliseconds())
		m.op.Log.Info("diff de contratos", atributos...)
	}

	if tipo, _, err := mime.ParseMediaType(r.Header.Get("Content-Type")); err != nil || tipo != tipoContenidoEsperado {
		registrar(resultadoSolicitud)
		problema(w, http.StatusUnsupportedMediaType, "El cuerpo debe ser application/json", "", "")
		return
	}

	var sol Solicitud
	decodificador := json.NewDecoder(http.MaxBytesReader(w, r.Body, m.op.MaxBytes))
	decodificador.DisallowUnknownFields()
	if err := decodificador.Decode(&sol); err != nil {
		var demasiado *http.MaxBytesError
		if errors.As(err, &demasiado) {
			registrar(resultadoGrande)
			problema(w, http.StatusRequestEntityTooLarge, "Las specs superan el tamaño máximo",
				"El cuerpo admite hasta "+strconv.FormatInt(m.op.MaxBytes, 10)+" bytes.", "")
			return
		}
		registrar(resultadoSolicitud)
		problema(w, http.StatusBadRequest, "El cuerpo no es un JSON válido con tipo, base y revision", "", "")
		return
	}

	tipo, idioma, err := ValidarTipoEIdioma(sol.Tipo, sol.Idioma)
	if tipo == TipoOpenAPI || tipo == TipoAsyncAPI || tipo == TipoProto {
		tipoMetrica = tipo
	}
	switch {
	case errors.Is(err, ErrTipoNoSoportado):
		registrar(resultadoNoSoportado)
		problema(w, http.StatusUnprocessableEntity, "Diff no soportado para este tipo",
			"Por ahora solo hay diff de OpenAPI; AsyncAPI y proto quedan pendientes (ATLAS-ADR-0036).", "")
		return
	case err != nil:
		registrar(resultadoSolicitud)
		problema(w, http.StatusBadRequest, "Tipo o idioma no válidos", "tipo: openapi, asyncapi o proto; idioma: es o en.", "")
		return
	case sol.Base == "" || sol.Revision == "":
		registrar(resultadoSolicitud)
		problema(w, http.StatusBadRequest, "Faltan las specs", "base y revision son obligatorias.", "")
		return
	}

	base, revision := []byte(sol.Base), []byte(sol.Revision)
	hashBase, hashRevision := Hash(base), Hash(revision)
	atributos := []any{
		"idioma", idioma,
		"hash_base", hashBase[:longitudPrefijoHashLogs], "hash_revision", hashRevision[:longitudPrefijoHashLogs],
		"bytes_base", len(base), "bytes_revision", len(revision),
	}
	clave := ClaveDiff(tipo, idioma, m.motor, hashBase, hashRevision)

	if cuerpo, ok := m.op.Cache.Obtener(clave); ok {
		m.metricas.duracion.WithLabelValues("acierto").Observe(time.Since(inicio).Seconds())
		registrar(resultadoCache, atributos...)
		responder(w, cuerpo, "acierto")
		return
	}

	ctx, cancelar := context.WithTimeout(r.Context(), m.op.Tiempo)
	defer cancelar()

	select {
	case m.turnos <- struct{}{}:
	case <-ctx.Done():
		registrar(resultadoOcupado, atributos...)
		w.Header().Set("Retry-After", "5")
		problema(w, http.StatusServiceUnavailable, "atlas-render está ocupado calculando otros diffs", "Reintenta en unos segundos.", "")
		return
	}

	// El cálculo sigue en su goroutine aunque se agote el tiempo: su turno se libera al terminar (así la concurrencia
	// real nunca supera el tope) y el resultado queda en la caché para el reintento.
	listo := make(chan salida, 1)
	m.metricas.enCurso.Inc()
	go func() {
		defer func() {
			m.metricas.enCurso.Dec()
			<-m.turnos
		}()
		res, err := m.op.Calcular(base, revision, idioma)
		if err != nil {
			listo <- salida{err: err}
			return
		}
		cuerpo, err := json.Marshal(res)
		if err == nil {
			m.op.Cache.Guardar(clave, cuerpo)
		}
		listo <- salida{cuerpo: cuerpo, resultado: res, err: err}
	}()

	select {
	case s := <-listo:
		var invalida *SpecInvalidaError
		switch {
		case errors.As(s.err, &invalida):
			registrar(resultadoSpecInvalida, append(atributos, "lado", invalida.Lado)...)
			problema(w, http.StatusUnprocessableEntity, "La spec "+invalida.Lado+" no es un OpenAPI válido",
				recortar(invalida.Causa.Error()), invalida.Lado)
		case s.err != nil:
			// El error de la librería puede citar partes de la spec: no va a los logs ni a la respuesta.
			registrar(resultadoErrorInterno, atributos...)
			problema(w, http.StatusInternalServerError, "No se pudo calcular el diff", "", "")
		default:
			m.metricas.duracion.WithLabelValues("fallo").Observe(time.Since(inicio).Seconds())
			registrar(resultadoOK, append(atributos, "cambios", s.resultado.Resumen.Total,
				"breaking", s.resultado.Resumen.Breaking)...)
			responder(w, s.cuerpo, "fallo")
		}
	case <-ctx.Done():
		registrar(resultadoTiempoAgotado, atributos...)
		problema(w, http.StatusGatewayTimeout, "El diff tardó demasiado",
			"Límite: "+m.op.Tiempo.String()+". El cálculo sigue y el resultado quedará en caché.", "")
	}
}

func responder(w http.ResponseWriter, cuerpo []byte, cache string) {
	w.Header().Set("Content-Type", tipoContenidoRespuesta)
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set(encabezadoCache, cache)
	_, _ = w.Write(cuerpo)
}

func problema(w http.ResponseWriter, estado int, titulo, detalle, lado string) {
	w.Header().Set("Content-Type", tipoContenidoProblema)
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(estado)
	_ = json.NewEncoder(w).Encode(Problema{Titulo: titulo, Estado: estado, Detalle: detalle, Lado: lado})
}

func recortar(texto string) string {
	r := []rune(texto)
	if len(r) <= maxDetalleError {
		return texto
	}
	return string(r[:maxDetalleError]) + "…"
}
