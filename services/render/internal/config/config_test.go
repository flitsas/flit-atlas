package config

import (
	"errors"
	"reflect"
	"testing"
	"time"
)

func entorno(valores map[string]string) func(string) (string, bool) {
	return func(k string) (string, bool) {
		v, ok := valores[k]
		return v, ok
	}
}

func TestCargarSinVariablesNombraLasQueFaltan(t *testing.T) {
	_, err := Cargar(entorno(map[string]string{"ATLAS_RENDER_PORT": "  "}))

	var faltantes *FaltantesError
	if !errors.As(err, &faltantes) {
		t.Fatalf("se esperaba FaltantesError, llegó %v", err)
	}
	if !reflect.DeepEqual(faltantes.Variables, []string{"ATLAS_RENDER_PORT"}) {
		t.Fatalf("variables = %v", faltantes.Variables)
	}
}

func TestCargarRechazaPuertoInvalido(t *testing.T) {
	for _, valor := range []string{"abc", "0", "70000"} {
		if _, err := Cargar(entorno(map[string]string{"ATLAS_RENDER_PORT": valor})); err == nil {
			t.Errorf("puerto %q debería ser inválido", valor)
		}
	}
}

func TestCargarConPuertoValido(t *testing.T) {
	cfg, err := Cargar(entorno(map[string]string{"ATLAS_RENDER_PORT": "4033"}))
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Puerto != 4033 {
		t.Fatalf("puerto = %d", cfg.Puerto)
	}
	if cfg.Diff.MaxBytes != DiffMaxBytesPorDefecto || cfg.Diff.Tiempo != DiffTiempoPorDefecto ||
		cfg.Diff.CacheEntradas != DiffCacheEntradasPorDefecto || cfg.Diff.Concurrencia < 1 {
		t.Fatalf("valores por defecto del diff = %+v", cfg.Diff)
	}
}

func TestCargarLeeLosLimitesDelDiff(t *testing.T) {
	cfg, err := Cargar(entorno(map[string]string{
		"ATLAS_RENDER_PORT":                 "4033",
		"ATLAS_RENDER_DIFF_MAX_BYTES":       "1024",
		"ATLAS_RENDER_DIFF_TIEMPO":          "2s",
		"ATLAS_RENDER_DIFF_CONCURRENCIA":    "3",
		"ATLAS_RENDER_DIFF_CACHE_ENTRADAS":  "0",
		"ATLAS_RENDER_DIFF_CACHE_MAX_BYTES": "2048",
	}))
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Diff.MaxBytes != 1024 || cfg.Diff.Tiempo != 2*time.Second || cfg.Diff.Concurrencia != 3 ||
		cfg.Diff.CacheEntradas != 0 || cfg.Diff.CacheMaxBytes != 2048 {
		t.Fatalf("diff = %+v", cfg.Diff)
	}
}

func TestCargarRechazaLimitesDelDiffInvalidos(t *testing.T) {
	casos := map[string]string{
		"ATLAS_RENDER_DIFF_MAX_BYTES":    "0",
		"ATLAS_RENDER_DIFF_TIEMPO":       "quince",
		"ATLAS_RENDER_DIFF_CONCURRENCIA": "-1",
	}
	for variable, valor := range casos {
		if _, err := Cargar(entorno(map[string]string{"ATLAS_RENDER_PORT": "4033", variable: valor})); err == nil {
			t.Errorf("%s=%q debería ser inválido", variable, valor)
		}
	}
}
