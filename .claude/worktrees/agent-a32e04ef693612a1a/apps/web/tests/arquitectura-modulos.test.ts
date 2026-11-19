import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { MODULOS_REGISTRADOS } from '../src/app/modulos/registro';

/**
 * HU 1.9 (ATLAS-ADR-0016 y ATLAS-ADR-0026): un módulo del frontend se agrega sin tocar los demás. Ningún módulo importa
 * a otro (solo al shell, `src/app/`), y el shell solo los conoce por `src/app/modulos/registro.ts`.
 */
const SRC = resolve(dirname(fileURLToPath(import.meta.url)), '../src');
const MODULOS = join(SRC, 'modulos');

function archivos(carpeta: string): string[] {
  return readdirSync(carpeta).flatMap((nombre) => {
    const ruta = join(carpeta, nombre);
    if (statSync(ruta).isDirectory()) return archivos(ruta);
    return /\.(ts|tsx)$/.test(nombre) && !/\.test\.tsx?$/.test(nombre) ? [ruta] : [];
  });
}

/** Imports relativos de un archivo, resueltos a rutas absolutas. */
function importsRelativos(archivo: string): string[] {
  const codigo = readFileSync(archivo, 'utf8');
  return [...codigo.matchAll(/(?:from\s+|import\s*\(\s*)['"](\.[^'"]+)['"]/g)].map((m) => resolve(dirname(archivo), m[1]!));
}

/** Módulo (carpeta de `src/modulos/`) al que pertenece una ruta, o `undefined` si está fuera. */
function moduloDe(ruta: string): string | undefined {
  const rel = relative(MODULOS, ruta);
  return rel.startsWith('..') ? undefined : rel.split(sep)[0];
}

describe('arquitectura de los módulos del frontend', () => {
  it('cada carpeta de src/modulos está registrada y cada módulo registrado tiene su carpeta', () => {
    const carpetas = readdirSync(MODULOS).filter((n) => statSync(join(MODULOS, n)).isDirectory());
    expect(carpetas.sort()).toEqual(MODULOS_REGISTRADOS.map((m) => m.id).sort());
  });

  it('ningún módulo importa a otro: solo a sí mismo y al shell (src/app, src/pwa, src/marca)', () => {
    const shell = ['app', 'pwa', 'marca'].map((c) => join(SRC, c) + sep);
    const problemas = archivos(MODULOS).flatMap((archivo) => {
      const propio = moduloDe(archivo);
      return importsRelativos(archivo)
        .filter((destino) => moduloDe(destino) !== propio && !shell.some((c) => destino.startsWith(c)))
        .map((destino) => `${relative(SRC, archivo)} → ${relative(SRC, destino)}`);
    });
    expect(problemas).toEqual([]);
  });

  it('el shell solo conoce los módulos por src/app/modulos/registro.ts', () => {
    const problemas = archivos(join(SRC, 'app')).flatMap((archivo) =>
      importsRelativos(archivo)
        .filter((destino) => moduloDe(destino) !== undefined && !archivo.endsWith(join('app', 'modulos', 'registro.ts')))
        .map((destino) => `${relative(SRC, archivo)} → ${relative(SRC, destino)}`),
    );
    expect(problemas).toEqual([]);
  });
});
