// Package config lee la configuración de atlas-render desde variables de entorno.
package config

import (
	"fmt"
	"runtime"
	"strconv"
	"strings"
	"time"
)

// Config es la configuración de atlas-render.
type Config struct {
	// Puerto HTTP interno (health, métricas y endpoints internos). Propuesta F0.3: 4033 / 5033 / 6033.
	Puerto int
	// Diff es la configuración del diff de contratos (HU 3.5).
	Diff Diff
}

// Diff agrupa los límites del endpoint de diff de contratos. Todas tienen valor por defecto.
type Diff struct {
	// MaxBytes es el tamaño máximo del cuerpo de la petición (las dos specs juntas). ATLAS_RENDER_DIFF_MAX_BYTES.
	MaxBytes int64
	// Tiempo es el tiempo máximo para responder un diff, incluida la espera de turno. ATLAS_RENDER_DIFF_TIEMPO.
	Tiempo time.Duration
	// Concurrencia es cuántos diffs se calculan a la vez como máximo. ATLAS_RENDER_DIFF_CONCURRENCIA.
	Concurrencia int
	// CacheEntradas es el tope de resultados en la caché LRU. ATLAS_RENDER_DIFF_CACHE_ENTRADAS (0 la apaga).
	CacheEntradas int
	// CacheMaxBytes es el tope de bytes de la caché LRU. ATLAS_RENDER_DIFF_CACHE_MAX_BYTES.
	CacheMaxBytes int64
}

// Valores por defecto del diff.
const (
	DiffMaxBytesPorDefecto      = 8 << 20
	DiffTiempoPorDefecto        = 15 * time.Second
	DiffCacheEntradasPorDefecto = 256
	DiffCacheBytesPorDefecto    = 64 << 20
)

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

	diff, err := cargarDiff(buscar)
	if err != nil {
		return Config{}, err
	}
	return Config{Puerto: puerto, Diff: diff}, nil
}

func cargarDiff(buscar func(string) (string, bool)) (Diff, error) {
	d := Diff{
		MaxBytes:      DiffMaxBytesPorDefecto,
		Tiempo:        DiffTiempoPorDefecto,
		Concurrencia:  runtime.NumCPU(),
		CacheEntradas: DiffCacheEntradasPorDefecto,
		CacheMaxBytes: DiffCacheBytesPorDefecto,
	}
	var err error
	if d.MaxBytes, err = entero(buscar, "ATLAS_RENDER_DIFF_MAX_BYTES", d.MaxBytes, 1); err != nil {
		return Diff{}, err
	}
	if d.CacheMaxBytes, err = entero(buscar, "ATLAS_RENDER_DIFF_CACHE_MAX_BYTES", d.CacheMaxBytes, 0); err != nil {
		return Diff{}, err
	}
	entradas, err := entero(buscar, "ATLAS_RENDER_DIFF_CACHE_ENTRADAS", int64(d.CacheEntradas), 0)
	if err != nil {
		return Diff{}, err
	}
	d.CacheEntradas = int(entradas)
	concurrencia, err := entero(buscar, "ATLAS_RENDER_DIFF_CONCURRENCIA", int64(d.Concurrencia), 1)
	if err != nil {
		return Diff{}, err
	}
	d.Concurrencia = int(concurrencia)
	if texto, ok := buscar("ATLAS_RENDER_DIFF_TIEMPO"); ok && strings.TrimSpace(texto) != "" {
		tiempo, errTiempo := time.ParseDuration(strings.TrimSpace(texto))
		if errTiempo != nil || tiempo <= 0 {
			return Diff{}, fmt.Errorf("ATLAS_RENDER_DIFF_TIEMPO inválido: %q (usa una duración de Go, p. ej. 15s)", texto)
		}
		d.Tiempo = tiempo
	}
	return d, nil
}

// entero lee una variable opcional entera; si no está, devuelve el valor por defecto.
func entero(buscar func(string) (string, bool), nombre string, porDefecto, minimo int64) (int64, error) {
	texto, ok := buscar(nombre)
	if !ok || strings.TrimSpace(texto) == "" {
		return porDefecto, nil
	}
	valor, err := strconv.ParseInt(strings.TrimSpace(texto), 10, 64)
	if err != nil || valor < minimo {
		return 0, fmt.Errorf("%s inválido: %q", nombre, texto)
	}
	return valor, nil
}
