package diferencias

import "testing"

func TestCacheExpulsaElMenosUsadoPorEntradas(t *testing.T) {
	c := NuevaCache(2, 0)
	c.Guardar("a", []byte("1"))
	c.Guardar("b", []byte("2"))
	c.Obtener("a") // a queda como la más reciente
	c.Guardar("c", []byte("3"))

	if _, ok := c.Obtener("b"); ok {
		t.Fatal("b debía salir: era la menos usada")
	}
	for _, k := range []string{"a", "c"} {
		if _, ok := c.Obtener(k); !ok {
			t.Fatalf("%s debía seguir", k)
		}
	}
	if n, b := c.Tamano(); n != 2 || b != 2 {
		t.Fatalf("tamaño = %d entradas, %d bytes", n, b)
	}
}

func TestCacheRespetaElTopeDeBytes(t *testing.T) {
	c := NuevaCache(10, 10)
	c.Guardar("a", make([]byte, 6))
	c.Guardar("b", make([]byte, 6))
	if _, ok := c.Obtener("a"); ok {
		t.Fatal("a debía salir por el tope de bytes")
	}
	c.Guardar("grande", make([]byte, 11))
	if _, ok := c.Obtener("grande"); ok {
		t.Fatal("un valor mayor que el tope no se guarda")
	}
	c.Guardar("b", make([]byte, 2))
	if _, b := c.Tamano(); b != 2 {
		t.Fatalf("reemplazar debe ajustar los bytes: %d", b)
	}
}

func TestCacheApagada(t *testing.T) {
	c := NuevaCache(0, 0)
	c.Guardar("a", []byte("1"))
	if _, ok := c.Obtener("a"); ok {
		t.Fatal("con 0 entradas la caché no guarda")
	}
}

func TestClaveDependeDeTodo(t *testing.T) {
	base := ClaveDiff("openapi", "es", "oasdiff v1", "h1", "h2")
	for _, otra := range []string{
		ClaveDiff("openapi", "en", "oasdiff v1", "h1", "h2"),
		ClaveDiff("openapi", "es", "oasdiff v2", "h1", "h2"),
		ClaveDiff("openapi", "es", "oasdiff v1", "h2", "h1"),
	} {
		if otra == base {
			t.Fatalf("clave repetida: %s", otra)
		}
	}
}
