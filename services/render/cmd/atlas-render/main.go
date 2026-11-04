// atlas-render: servicio interno de render (D2) y validación de contratos (vacuum, oasdiff) de Flit Atlas.
// Expone las sondas de salud, las métricas de Prometheus en /metrics y el diff de contratos (HU 3.5).
package main

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/flitsas/flit-atlas/services/render/internal/config"
	"github.com/flitsas/flit-atlas/services/render/internal/diferencias"
	"github.com/flitsas/flit-atlas/services/render/internal/salud"
	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/collectors"
	"github.com/prometheus/client_golang/prometheus/promhttp"
)

func main() {
	log := slog.New(slog.NewJSONHandler(os.Stdout, nil))
	if err := ejecutar(log); err != nil {
		log.Error("atlas-render terminó con error", "error", err.Error())
		os.Exit(1)
	}
}

func ejecutar(log *slog.Logger) error {
	cfg, err := config.Cargar(os.LookupEnv)
	if err != nil {
		return err
	}

	registro := prometheus.NewRegistry()
	registro.MustRegister(collectors.NewGoCollector(), collectors.NewProcessCollector(collectors.ProcessCollectorOpts{}))

	mux := http.NewServeMux()
	salud.Registrar(mux)
	mux.Handle("GET /metrics", promhttp.HandlerFor(registro, promhttp.HandlerOpts{}))
	diferencias.Registrar(mux, diferencias.Opciones{
		MaxBytes:     cfg.Diff.MaxBytes,
		Tiempo:       cfg.Diff.Tiempo,
		Concurrencia: cfg.Diff.Concurrencia,
		Cache:        diferencias.NuevaCache(cfg.Diff.CacheEntradas, cfg.Diff.CacheMaxBytes),
		Log:          log,
		Registro:     registro,
	})

	servidor := &http.Server{
		Addr:              fmt.Sprintf(":%d", cfg.Puerto),
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       30 * time.Second,
		// La respuesta del diff sale a más tardar al vencer su tiempo; el margen cubre el envío.
		WriteTimeout: cfg.Diff.Tiempo + 10*time.Second,
		IdleTimeout:  2 * time.Minute,
	}

	ctx, cancelar := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancelar()

	errServidor := make(chan error, 1)
	go func() {
		log.Info("atlas-render escuchando", "puerto", cfg.Puerto, "motor_diff", "oasdiff "+diferencias.VersionOasdiff())
		errServidor <- servidor.ListenAndServe()
	}()

	select {
	case err := <-errServidor:
		if !errors.Is(err, http.ErrServerClosed) {
			return err
		}
	case <-ctx.Done():
		apagar, cancelarApagado := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancelarApagado()
		return servidor.Shutdown(apagar)
	}
	return nil
}
