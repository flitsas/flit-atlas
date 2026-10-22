// Package salud publica las sondas /health/live y /health/ready.
package salud

import "net/http"

// Registrar agrega las sondas al mux. atlas-render no tiene dependencias externas todavía,
// así que "listo" equivale a "vivo"; cuando use caché o cola, ready las comprobará.
func Registrar(mux *http.ServeMux) {
	ok := func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "text/plain; charset=utf-8")
		w.Header().Set("Cache-Control", "no-store")
		_, _ = w.Write([]byte("Healthy"))
	}
	mux.HandleFunc("GET /health/live", ok)
	mux.HandleFunc("GET /health/ready", ok)
}
