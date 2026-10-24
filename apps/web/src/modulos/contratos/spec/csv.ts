/**
 * Exportar a CSV (HU 3.9): con BOM para que Excel en español abra bien las tildes, separador «;» (el de la
 * configuración regional de Colombia en Excel) y comillas cuando hace falta. Un valor que empiece por `=`, `+`, `-` o `@`
 * se antepone con `'` para que Excel no lo tome como fórmula (inyección de CSV).
 */
export function aCsv(encabezados: string[], filas: Array<Array<string | undefined>>): string {
  const celda = (valor: string | undefined) => {
    let t = valor ?? '';
    if (/^[=+\-@\t\r]/.test(t)) t = `'${t}`;
    return /[";\n\r]/.test(t) ? `"${t.replace(/"/g, '""')}"` : t;
  };
  return `﻿${[encabezados, ...filas].map((f) => f.map(celda).join(';')).join('\r\n')}\r\n`;
}

/** Descarga un texto como archivo (funciona en iOS: enlace temporal con `download`). */
export function descargar(nombre: string, contenido: string, tipo = 'text/csv;charset=utf-8') {
  const url = URL.createObjectURL(new Blob([contenido], { type: tipo }));
  const enlace = document.createElement('a');
  enlace.href = url;
  enlace.download = nombre;
  document.body.append(enlace);
  enlace.click();
  enlace.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
