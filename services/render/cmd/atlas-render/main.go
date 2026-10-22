// atlas-render: servicio interno de render (D2) y validación de contratos (vacuum, oasdiff) de Flit Atlas.
// En la HU 1.1 solo expone las sondas de salud.
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
	"github.com/flitsas/flit-atlas/services/render/internal/salud"
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

	mux := http.NewServeMux()
	salud.Registrar(mux)

	servidor := &http.Server{
		Addr:              fmt.Sprintf(":%d", cfg.Puerto),
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
	}

	ctx, cancelar := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer cancelar()

	errServidor := make(chan error, 1)
	go func() {
		log.Info("atlas-render escuchando", "puerto", cfg.Puerto)
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
