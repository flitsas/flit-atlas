package config

import (
	"errors"
	"reflect"
	"testing"
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
}
