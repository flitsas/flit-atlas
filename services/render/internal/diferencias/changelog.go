package diferencias

import (
	"fmt"
	"strings"
)

// Changelog arma el changelog legible en Markdown a partir del resultado. Los textos de cada cambio son los de oasdiff
// en el idioma pedido (en español, lo que oasdiff no tenga traducido queda en inglés); los títulos y el resumen los
// pone Atlas, porque la plantilla Markdown de oasdiff solo existe en inglés.
func Changelog(r *Resultado) string {
	var b strings.Builder
	ingles := r.Idioma == IdiomaIngles

	if ingles {
		fmt.Fprintf(&b, "# API changelog %s → %s\n\n", r.VersionBase, r.VersionRevision)
	} else {
		fmt.Fprintf(&b, "# Cambios del contrato %s → %s\n\n", r.VersionBase, r.VersionRevision)
	}

	if len(r.Cambios) == 0 {
		switch {
		case r.SinDiferencias && ingles:
			b.WriteString("No changes detected.\n")
		case r.SinDiferencias:
			b.WriteString("Sin cambios.\n")
		case ingles:
			b.WriteString("No changes to report, but the specs are different.\n")
		default:
			b.WriteString("Sin cambios que reportar, aunque las specs son distintas (p. ej. descripciones o ejemplos).\n")
		}
		return b.String()
	}

	s := r.Resumen
	if ingles {
		fmt.Fprintf(&b, "%d changes: %d breaking (%d error, %d warning), %d info.\n", s.Total, s.Breaking, s.Errores, s.Advertencias, s.Informativos)
	} else {
		fmt.Fprintf(&b, "%d cambios: %d incompatibles (%d error, %d warning) y %d informativos.\n", s.Total, s.Breaking, s.Errores, s.Advertencias, s.Informativos)
	}

	// Agrupa por operación en el orden en que oasdiff los entrega; lo que no es de una operación va por sección.
	type grupo struct {
		titulo  string
		cambios []Cambio
	}
	var grupos []*grupo
	indice := map[string]*grupo{}
	for _, c := range r.Cambios {
		titulo := c.Metodo + " " + c.Ruta
		if c.Ruta == "" {
			titulo = tituloSeccion(c.Seccion, ingles)
		}
		g, ok := indice[titulo]
		if !ok {
			g = &grupo{titulo: titulo}
			indice[titulo] = g
			grupos = append(grupos, g)
		}
		g.cambios = append(g.cambios, c)
	}

	for _, g := range grupos {
		fmt.Fprintf(&b, "\n## %s\n\n", g.titulo)
		for _, c := range g.cambios {
			if c.Breaking {
				fmt.Fprintf(&b, "- **%s** %s (`%s`)\n", c.Nivel, c.Texto, c.Regla)
			} else {
				fmt.Fprintf(&b, "- %s (`%s`)\n", c.Texto, c.Regla)
			}
		}
	}
	return b.String()
}

func tituloSeccion(seccion string, ingles bool) string {
	if ingles {
		if seccion == "" {
			return "Other"
		}
		return strings.ToUpper(seccion[:1]) + seccion[1:]
	}
	switch seccion {
	case "components":
		return "Componentes"
	case "security":
		return "Seguridad"
	case "paths":
		return "Rutas"
	case "":
		return "Otros"
	default:
		return strings.ToUpper(seccion[:1]) + seccion[1:]
	}
}
