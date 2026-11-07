package lienzo

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"mime"
	"net/http"
	"strconv"
	"time"

	"github.com/flitsas/flit-atlas/services/render/internal/diferencias"
	"github.com/prometheus/client_golang/prometheus"
)

// Ruta del endpoint interno. No se expone a internet: lo llama atlas-api por la red del clúster.
const Ruta = "/internal/v1/diagramas/lienzo/svg"

// Valores por defecto. El acomodo corre JavaScript en goja, así que pocas a la vez.
const (
	MaxBytesPorDefecto     = 2 << 20
	TiempoPorDefecto       = 30 * time.Second
	ConcurrenciaPorDefecto = 2
	CacheEntradasDefecto   = 128
	CacheBytesDefecto      = 64 << 20
	encabezadoCache        = "X-Atlas-Cache"
	tipoProblema           = "application/problem+json"
	tipoSVG                = "image/svg+xml"
	longitudHashLogs       = 12
)

// Opciones del manejador.
type Opciones struct {
	MaxBytes     int64
	Tiempo       time.Duration
	Concurrencia int
	Cache        *diferencias.Cache
	Log          *slog.Logger
	Registro     prometheus.Registerer
	// Renderizar permite sustituir el motor en las pruebas.
	Renderizar func(context.Context, Modelo) ([]byte, error)
}

type manejador struct {
	op         Opciones
	turnos     chan struct{}
	peticiones *prometheus.CounterVec
}

// Registrar crea el manejador y lo monta en el mux.
func Registrar(mux *http.ServeMux, op Opciones) {
	mux.Handle("POST "+Ruta, Nuevo(op))
}

// Nuevo crea el manejador y registra sus métricas.
func Nuevo(op Opciones) http.Handler {
	if op.MaxBytes <= 0 {
		op.MaxBytes = MaxBytesPorDefecto
	}
	if op.Tiempo <= 0 {
		op.Tiempo = TiempoPorDefecto
	}
	if op.Concurrencia < 1 {
		op.Concurrencia = ConcurrenciaPorDefecto
	}
	if op.Cache == nil {
		op.Cache = diferencias.NuevaCache(CacheEntradasDefecto, CacheBytesDefecto)
	}
	if op.Log == nil {
		op.Log = slog.New(slog.DiscardHandler)
	}
	if op.Renderizar == nil {
		op.Renderizar = Renderizar
	}
	m := &manejador{op: op, turnos: make(chan struct{}, op.Concurrencia), peticiones: prometheus.NewCounterVec(prometheus.CounterOpts{
		Name: "atlas_render_lienzo_peticiones_total",
		Help: "Exportes de lienzos a SVG por resultado.",
	}, []string{"resultado"})}
	if op.Registro != nil {
		op.Registro.MustRegister(m.peticiones)
	}
	return m
}

type salida struct {
	svg []byte
	err error
}

func (m *manejador) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	inicio := time.Now()
	registrar := func(resultado, hash string) {
		m.peticiones.WithLabelValues(resultado).Inc()
		// Nunca las etiquetas: solo el resultado, el prefijo del hash y la duración.
		m.op.Log.Info("exporte de lienzo", "resultado", resultado, "hash", hash, "duracion_ms", time.Since(inicio).Milliseconds())
	}
	if tipo, _, err := mime.ParseMediaType(r.Header.Get("Content-Type")); err != nil || tipo != "application/json" {
		registrar("solicitud_invalida", "")
		problema(w, http.StatusUnsupportedMediaType, "El cuerpo debe ser application/json")
		return
	}
	var modelo Modelo
	decodificador := json.NewDecoder(http.MaxBytesReader(w, r.Body, m.op.MaxBytes))
	decodificador.DisallowUnknownFields()
	if err := decodificador.Decode(&modelo); err != nil {
		var demasiado *http.MaxBytesError
		if errors.As(err, &demasiado) {
			registrar("demasiado_grande", "")
			problema(w, http.StatusRequestEntityTooLarge, "El modelo supera "+strconv.FormatInt(m.op.MaxBytes, 10)+" bytes")
			return
		}
		registrar("solicitud_invalida", "")
		problema(w, http.StatusBadRequest, "El cuerpo no es un modelo de lienzo válido")
		return
	}
	if _, err := GenerarD2(modelo); err != nil {
		registrar("modelo_invalido", "")
		problema(w, http.StatusUnprocessableEntity, "El modelo no cumple los topes o tiene relaciones sueltas")
		return
	}

	// Se serializa de nuevo: el mismo modelo da el mismo hash aunque el JSON venga con otros espacios.
	canonico, err := json.Marshal(modelo)
	if err != nil {
		registrar("error", "")
		problema(w, http.StatusInternalServerError, "No se pudo leer el modelo")
		return
	}
	hash := diferencias.Hash(canonico)
	prefijo := hash[:longitudHashLogs]
	clave := "lienzo|" + VersionD2() + "|" + hash
	if svg, ok := m.op.Cache.Obtener(clave); ok {
		registrar("cache", prefijo)
		responder(w, svg, "acierto")
		return
	}

	ctx, cancelar := context.WithTimeout(r.Context(), m.op.Tiempo)
	defer cancelar()
	select {
	case m.turnos <- struct{}{}:
	case <-ctx.Done():
		registrar("ocupado", prefijo)
		w.Header().Set("Retry-After", "5")
		problema(w, http.StatusServiceUnavailable, "atlas-render está ocupado")
		return
	}

	// Sigue aunque se agote el tiempo, sin el contexto de la petición: libera su turno al terminar y deja el resultado en la
	// caché para el reintento. D2 corre JavaScript: un panic no debe tumbar el servicio.
	listo := make(chan salida, 1)
	go func() {
		defer func() { <-m.turnos }()
		defer func() {
			if causa := recover(); causa != nil {
				listo <- salida{err: fmt.Errorf("render: %T", causa)}
			}
		}()
		svg, err := m.op.Renderizar(context.WithoutCancel(ctx), modelo)
		if err == nil {
			m.op.Cache.Guardar(clave, svg)
		}
		listo <- salida{svg: svg, err: err}
	}()

	select {
	case s := <-listo:
		if s.err != nil {
			// El error de la librería puede citar las etiquetas: no va a los logs ni a la respuesta.
			registrar("error", prefijo)
			problema(w, http.StatusInternalServerError, "No se pudo generar el SVG")
			return
		}
		registrar("ok", prefijo)
		responder(w, s.svg, "fallo")
	case <-ctx.Done():
		registrar("tiempo_agotado", prefijo)
		problema(w, http.StatusGatewayTimeout, "El SVG tardó demasiado; el cálculo sigue y quedará en caché")
	}
}

func responder(w http.ResponseWriter, svg []byte, cache string) {
	w.Header().Set("Content-Type", tipoSVG)
	w.Header().Set("Cache-Control", "no-store")
	w.Header().Set("X-Content-Type-Options", "nosniff")
	w.Header().Set(encabezadoCache, cache)
	_, _ = w.Write(svg)
}

func problema(w http.ResponseWriter, estado int, titulo string) {
	w.Header().Set("Content-Type", tipoProblema)
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(estado)
	_ = json.NewEncoder(w).Encode(diferencias.Problema{Titulo: titulo, Estado: estado})
}
