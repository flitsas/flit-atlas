package diferencias

import (
	"container/list"
	"sync"
)

// Cache es una caché LRU en memoria, segura entre goroutines, con tope de entradas y de bytes. Guarda la respuesta
// ya serializada: el diff de un par de specs (por sus SHA-256) con el mismo motor e idioma siempre da lo mismo, así
// que perder la caché (reinicio, otra réplica) solo cuesta volver a calcular (ATLAS-ADR-0036).
type Cache struct {
	mu         sync.Mutex
	maxEntrada int
	maxBytes   int64
	bytes      int64
	orden      *list.List
	elementos  map[string]*list.Element
}

type entradaCache struct {
	clave string
	valor []byte
}

// NuevaCache crea la caché. Con maxEntradas 0 la caché queda apagada (nunca guarda).
func NuevaCache(maxEntradas int, maxBytes int64) *Cache {
	return &Cache{
		maxEntrada: maxEntradas,
		maxBytes:   maxBytes,
		orden:      list.New(),
		elementos:  map[string]*list.Element{},
	}
}

// ClaveDiff arma la clave de un diff: tipo, idioma, motor y los dos hashes. El orden importa (base → revisión).
func ClaveDiff(tipo, idioma, motor, hashBase, hashRevision string) string {
	return tipo + "|" + idioma + "|" + motor + "|" + hashBase + "|" + hashRevision
}

// Obtener devuelve el valor y lo marca como usado recientemente.
func (c *Cache) Obtener(clave string) ([]byte, bool) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if e, ok := c.elementos[clave]; ok {
		c.orden.MoveToFront(e)
		return e.Value.(*entradaCache).valor, true
	}
	return nil, false
}

// Guardar agrega o reemplaza un valor y expulsa los menos usados hasta respetar los topes. Un valor más grande que
// el tope de bytes no se guarda.
func (c *Cache) Guardar(clave string, valor []byte) {
	tamano := int64(len(valor))
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.maxEntrada <= 0 || (c.maxBytes > 0 && tamano > c.maxBytes) {
		return
	}
	if e, ok := c.elementos[clave]; ok {
		anterior := e.Value.(*entradaCache)
		c.bytes += tamano - int64(len(anterior.valor))
		anterior.valor = valor
		c.orden.MoveToFront(e)
	} else {
		c.elementos[clave] = c.orden.PushFront(&entradaCache{clave: clave, valor: valor})
		c.bytes += tamano
	}
	for c.orden.Len() > c.maxEntrada || (c.maxBytes > 0 && c.bytes > c.maxBytes) {
		ultimo := c.orden.Back()
		if ultimo == nil {
			break
		}
		entrada := ultimo.Value.(*entradaCache)
		c.orden.Remove(ultimo)
		delete(c.elementos, entrada.clave)
		c.bytes -= int64(len(entrada.valor))
	}
}

// Tamano devuelve el número de entradas y los bytes ocupados.
func (c *Cache) Tamano() (int, int64) {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.orden.Len(), c.bytes
}
