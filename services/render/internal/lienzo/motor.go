// Package lienzo exporta los diagramas del lienzo de Atlas a SVG con D2 (HU 6.8, ATLAS-ADR-0054 §7): recibe el modelo, nunca
// texto D2, así que nadie puede usar imports ni enlaces de D2, y limpia el SVG antes de entregarlo.
package lienzo

import (
	"context"
	"errors"
	"fmt"
	"log/slog"
	"regexp"
	"runtime/debug"
	"strings"
	"sync"

	"oss.terrastruct.com/d2/d2graph"
	"oss.terrastruct.com/d2/d2layouts/d2dagrelayout"
	"oss.terrastruct.com/d2/d2lib"
	"oss.terrastruct.com/d2/d2renderers/d2svg"
	"oss.terrastruct.com/d2/lib/log"
	"oss.terrastruct.com/d2/lib/textmeasure"
	"oss.terrastruct.com/util-go/go2"
)

// Topes del modelo: los mismos de ValidadorLienzo en atlas-api.
const (
	MaximoNodos      = 500
	MaximoRelaciones = 1000
	maximoEtiqueta   = 500
)

// Nodo es un nodo del lienzo. El id solo sirve para unir las relaciones: no llega al D2.
type Nodo struct {
	ID       string `json:"id"`
	Tipo     string `json:"tipo"`
	Etiqueta string `json:"etiqueta"`
}

// Relacion une dos nodos por su id.
type Relacion struct {
	Origen   string `json:"origen"`
	Destino  string `json:"destino"`
	Etiqueta string `json:"etiqueta,omitempty"`
}

// Modelo es lo que se exporta.
type Modelo struct {
	Nodos      []Nodo     `json:"nodos"`
	Relaciones []Relacion `json:"relaciones"`
}

// ErrModeloInvalido es un modelo que no cumple los topes o tiene relaciones sueltas.
var ErrModeloInvalido = errors.New("modelo inválido")

var formas = map[string]string{"actor": "person", "sistema": "rectangle", "contenedor": "rectangle", "componente": "rectangle", "nota": "page"}

// GenerarD2 traduce el modelo a D2. Los nodos se llaman n0, n1… y las etiquetas van entre comillas simples, sin sustitución de
// variables ni palabras reservadas: una etiqueta no puede cambiar la estructura del diagrama.
func GenerarD2(m Modelo) (string, error) {
	if len(m.Nodos) == 0 || len(m.Nodos) > MaximoNodos || len(m.Relaciones) > MaximoRelaciones {
		return "", fmt.Errorf("%w: entre 1 y %d nodos, hasta %d relaciones", ErrModeloInvalido, MaximoNodos, MaximoRelaciones)
	}
	nombres := make(map[string]string, len(m.Nodos))
	var d2 strings.Builder
	d2.WriteString("direction: right\n")
	for i, n := range m.Nodos {
		forma, ok := formas[n.Tipo]
		if !ok || n.ID == "" {
			return "", fmt.Errorf("%w: nodo %d", ErrModeloInvalido, i)
		}
		if _, repetido := nombres[n.ID]; repetido {
			return "", fmt.Errorf("%w: nodo %d repetido", ErrModeloInvalido, i)
		}
		etiqueta := limpiarEtiqueta(n.Etiqueta)
		if etiqueta == "" {
			return "", fmt.Errorf("%w: nodo %d sin etiqueta", ErrModeloInvalido, i)
		}
		nombre := fmt.Sprintf("n%d", i)
		nombres[n.ID] = nombre
		fmt.Fprintf(&d2, "%s: %s {shape: %s}\n", nombre, comillas(etiqueta), forma)
	}
	for i, r := range m.Relaciones {
		origen, okOrigen := nombres[r.Origen]
		destino, okDestino := nombres[r.Destino]
		if !okOrigen || !okDestino {
			return "", fmt.Errorf("%w: relación %d", ErrModeloInvalido, i)
		}
		if etiqueta := limpiarEtiqueta(r.Etiqueta); etiqueta != "" {
			fmt.Fprintf(&d2, "%s -> %s: %s\n", origen, destino, comillas(etiqueta))
		} else {
			fmt.Fprintf(&d2, "%s -> %s\n", origen, destino)
		}
	}
	return d2.String(), nil
}

var espacios = regexp.MustCompile(`[\s\p{Cc}]+`)

// limpiarEtiqueta deja una sola línea, sin caracteres de control y con el tope de largo.
func limpiarEtiqueta(texto string) string {
	limpia := strings.TrimSpace(espacios.ReplaceAllString(texto, " "))
	if r := []rune(limpia); len(r) > maximoEtiqueta {
		return string(r[:maximoEtiqueta])
	}
	return limpia
}

func comillas(texto string) string {
	return "'" + strings.ReplaceAll(texto, "'", "''") + "'"
}

// Renderizar genera el SVG ya limpio.
func Renderizar(ctx context.Context, m Modelo) ([]byte, error) {
	fuente, err := GenerarD2(m)
	if err != nil {
		return nil, err
	}
	medidor, err := textmeasure.NewRuler()
	if err != nil {
		return nil, err
	}
	opciones := &d2svg.RenderOpts{Pad: go2.Pointer(int64(d2svg.DEFAULT_PADDING)), ThemeID: go2.Pointer(int64(0))}
	// Sin logger en el contexto, D2 escribe en stderr, y sus mensajes pueden citar las etiquetas.
	ctx = log.With(ctx, slog.New(slog.DiscardHandler))
	diagrama, _, err := d2lib.Compile(ctx, fuente, &d2lib.CompileOptions{
		Ruler: medidor,
		LayoutResolver: func(string) (d2graph.LayoutGraph, error) {
			return d2dagrelayout.DefaultLayout, nil
		},
	}, opciones)
	if err != nil {
		return nil, err
	}
	svg, err := d2svg.Render(diagrama, opciones)
	if err != nil {
		return nil, err
	}
	return Sanear(svg)
}

var versionD2 = sync.OnceValue(func() string {
	info, ok := debug.ReadBuildInfo()
	if !ok {
		return "desconocida"
	}
	for _, dep := range info.Deps {
		if dep.Path == "oss.terrastruct.com/d2" {
			return dep.Version
		}
	}
	return "desconocida"
})

// VersionD2 es la versión de D2 enlazada; va en la clave de la caché.
func VersionD2() string { return versionD2() }
