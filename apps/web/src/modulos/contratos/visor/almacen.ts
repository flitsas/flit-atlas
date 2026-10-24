/**
 * El visor corre con origen opaco (`sandbox="allow-scripts"`): leer `localStorage` o `sessionStorage` lanza
 * `SecurityError` y Scalar no arranca. Se reemplazan por un almacén en memoria antes de cargar Scalar (este módulo va
 * primero en `entrada.ts`): nada del visor queda guardado en el dispositivo.
 */
class AlmacenEnMemoria implements Storage {
  private readonly datos = new Map<string, string>();

  get length() {
    return this.datos.size;
  }

  clear() {
    this.datos.clear();
  }

  getItem(clave: string) {
    return this.datos.get(clave) ?? null;
  }

  key(indice: number) {
    return [...this.datos.keys()][indice] ?? null;
  }

  removeItem(clave: string) {
    this.datos.delete(clave);
  }

  setItem(clave: string, valor: string) {
    this.datos.set(clave, String(valor));
  }
}

for (const nombre of ['localStorage', 'sessionStorage'] as const) {
  let disponible = false;
  try {
    disponible = !!window[nombre];
  } catch {
    disponible = false;
  }
  if (!disponible) Object.defineProperty(window, nombre, { value: new AlmacenEnMemoria(), configurable: true });
}
