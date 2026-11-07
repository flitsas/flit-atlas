package lienzo

import (
	"bytes"
	"encoding/xml"
	"errors"
	"io"
	"regexp"
	"strings"
)

// ErrSVGInseguro es un SVG con CSS que no se puede limpiar sin romperlo: se rechaza entero.
var ErrSVGInseguro = errors.New("svg inseguro")

// elementosPermitidos es una lista blanca: lo demás (script, foreignObject, a, image, use, iframe, animate…) se quita con su
// contenido. Una lista negra envejece mal.
var elementosPermitidos = map[string]bool{
	"svg": true, "g": true, "path": true, "rect": true, "circle": true, "ellipse": true, "line": true, "polyline": true, "polygon": true,
	"text": true, "tspan": true, "defs": true, "style": true, "mask": true, "clipPath": true, "marker": true, "linearGradient": true,
	"radialGradient": true, "stop": true, "pattern": true, "title": true, "desc": true, "filter": true, "feGaussianBlur": true,
	"feOffset": true, "feBlend": true, "feFlood": true, "feComposite": true, "feMerge": true, "feMergeNode": true, "feColorMatrix": true,
}

var (
	urlCSS = regexp.MustCompile(`(?i)url\(\s*["']?([^"')\s]*)`)
	// Solo fuentes incrustadas y referencias internas.
	urlPermitida = regexp.MustCompile(`(?i)^(#|data:(application/font-woff|font/woff2?|font/ttf);base64,)`)
)

// Sanear devuelve el SVG sin scripts ni enlaces externos. Reescribe el documento desde sus elementos, con RawToken para no
// tocar los prefijos, y falla si el XML está mal formado. La DTD, los comentarios y las instrucciones se pierden.
func Sanear(svg []byte) ([]byte, error) {
	lector := xml.NewDecoder(bytes.NewReader(svg))
	var salida bytes.Buffer
	saltando, dentroDeEstilo, profundidad := 0, 0, 0
	for {
		token, err := lector.RawToken()
		if errors.Is(err, io.EOF) {
			break
		}
		if err != nil {
			return nil, err
		}
		switch t := token.(type) {
		case xml.StartElement:
			profundidad++
			if saltando > 0 {
				saltando++
				continue
			}
			if t.Name.Space != "" || !elementosPermitidos[t.Name.Local] {
				saltando = 1
				continue
			}
			if t.Name.Local == "style" {
				dentroDeEstilo++
			}
			atributos, err := atributosSeguros(t.Attr)
			if err != nil {
				return nil, err
			}
			salida.WriteByte('<')
			salida.WriteString(t.Name.Local)
			salida.WriteString(atributos)
			salida.WriteByte('>')
		case xml.EndElement:
			profundidad--
			if saltando > 0 {
				saltando--
				continue
			}
			if t.Name.Local == "style" && dentroDeEstilo > 0 {
				dentroDeEstilo--
			}
			salida.WriteString("</")
			salida.WriteString(t.Name.Local)
			salida.WriteByte('>')
		case xml.CharData:
			if saltando > 0 {
				continue
			}
			if dentroDeEstilo > 0 && !cssSeguro(string(t)) {
				return nil, ErrSVGInseguro
			}
			if err := xml.EscapeText(&salida, t); err != nil {
				return nil, err
			}
		}
	}
	if profundidad != 0 || salida.Len() == 0 {
		return nil, ErrSVGInseguro
	}
	return salida.Bytes(), nil
}

func atributosSeguros(atributos []xml.Attr) (string, error) {
	var salida bytes.Buffer
	for _, a := range atributos {
		nombre := a.Name.Local
		if a.Name.Space != "" {
			nombre = a.Name.Space + ":" + nombre
		}
		minuscula := strings.ToLower(nombre)
		switch {
		case strings.HasPrefix(minuscula, "on"):
			continue
		case minuscula == "href" || minuscula == "xlink:href" || minuscula == "src":
			if !strings.HasPrefix(strings.TrimSpace(a.Value), "#") {
				continue
			}
		case a.Name.Space == "xmlns" || nombre == "xmlns":
			// Las declaraciones de espacios de nombres se conservan solo para SVG y XLink.
			if a.Value != "http://www.w3.org/2000/svg" && a.Value != "http://www.w3.org/1999/xlink" {
				continue
			}
		case !cssSeguro(a.Value):
			continue
		}
		salida.WriteByte(' ')
		salida.WriteString(nombre)
		salida.WriteString(`="`)
		if err := xml.EscapeText(&salida, []byte(a.Value)); err != nil {
			return "", err
		}
		salida.WriteByte('"')
	}
	return salida.String(), nil
}

// cssSeguro revisa CSS o el valor de un atributo: nada de @import ni de url() hacia fuera. Las secuencias de escape de CSS
// permiten escribir «url» sin esas letras, así que también se rechazan.
func cssSeguro(texto string) bool {
	minuscula := strings.ToLower(texto)
	if strings.Contains(minuscula, "@import") || strings.Contains(minuscula, "expression(") ||
		strings.Contains(minuscula, "javascript:") || strings.Contains(minuscula, "\\") {
		return false
	}
	for _, encontrada := range urlCSS.FindAllStringSubmatch(texto, -1) {
		if !urlPermitida.MatchString(encontrada[1]) {
			return false
		}
	}
	return true
}
