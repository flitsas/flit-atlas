// Package config lee la configuración de atlas-render desde variables de entorno.
package config

import (
	"fmt"
	"strconv"
	"strings"
)

// Config es la configuración de atlas-render.
type Config struct {
	// Puerto HTTP interno (health y métricas). Propuesta F0.3: 4033 / 5033 / 6033.
	Puerto int
}

// Variables obligatorias. Se listan todas para que el error de arranque las nombre de una vez.
var requeridas = []string{"ATLAS_RENDER_PORT"}

// FaltantesError informa todas las variables obligatorias que faltan o son inválidas.
type FaltantesError struct {
	Variables []string
}

func (e *FaltantesError) Error() string {
	return "faltan variables de configuración obligatorias: " + strings.Join(e.Variables, ", ")
}

// Cargar construye la configuración con la función de búsqueda dada (os.LookupEnv en producción).
func Cargar(buscar func(string) (string, bool)) (Config, error) {
	var faltantes []string
	for _, v := range requeridas {
		if valor, ok := buscar(v); !ok || strings.TrimSpace(valor) == "" {
			faltantes = append(faltantes, v)
		}
	}
	if len(faltantes) > 0 {
		return Config{}, &FaltantesError{Variables: faltantes}
	}

	valor, _ := buscar("ATLAS_RENDER_PORT")
	puerto, err := strconv.Atoi(strings.TrimSpace(valor))
	if err != nil || puerto < 1 || puerto > 65535 {
		return Config{}, fmt.Errorf("ATLAS_RENDER_PORT inválido: %q", valor)
	}
	return Config{Puerto: puerto}, nil
}
