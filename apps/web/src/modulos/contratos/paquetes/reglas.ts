import type { ContratoElegible, GuiaElegible, RevisionPaqueteDetalle, SolicitudPaquete } from './api';

/**
 * Estado del asistente de paquetes (HU 4.1) y sus reglas puras: qué contratos, en qué versión, qué operaciones y qué
 * guías. El servidor vuelve a validar todo (ATLAS-ADR-0041); aquí solo se arma la selección.
 */
export interface SeleccionContrato {
  contratoId: string;
  versionId: string;
  operaciones: string[];
  /** `revision: null` = la publicada (la completa el servidor). */
  guias: Array<{ guiaId: string; revision: number | null }>;
}

export interface EstadoAsistente {
  nombre: string;
  descripcion: string;
  contratos: SeleccionContrato[];
  nota: string;
}

export const VACIO: EstadoAsistente = { nombre: '', descripcion: '', contratos: [], nota: '' };

/** El asistente sobre la última revisión (para guardar otra encima). */
export function estadoDesde(r: RevisionPaqueteDetalle): EstadoAsistente {
  return {
    nombre: r.nombre,
    descripcion: r.descripcion ?? '',
    contratos: r.definicion.map((e) => ({
      contratoId: e.contratoId,
      versionId: e.versionId,
      operaciones: [...e.operaciones],
      guias: e.guias.map((g) => ({ guiaId: g.guiaId, revision: g.revision })),
    })),
    nota: '',
  };
}

export function solicitudDe(estado: EstadoAsistente, revisionBase?: number): SolicitudPaquete {
  return {
    nombre: estado.nombre.trim(),
    descripcion: estado.descripcion.trim() || null,
    // Las guías van sin número: el servidor pone su revisión publicada (la única que puede entrar al guardar).
    definicion: estado.contratos.map((c) => ({
      contratoId: c.contratoId,
      versionId: c.versionId,
      operaciones: c.operaciones,
      guias: c.guias.map((g) => ({ guiaId: g.guiaId, revision: null })),
    })),
    nota: estado.nota.trim(),
    ...(revisionBase === undefined ? {} : { revisionBase }),
  };
}

const conContrato = (estado: EstadoAsistente, contratoId: string, cambio: (c: SeleccionContrato) => SeleccionContrato): EstadoAsistente => ({
  ...estado,
  contratos: estado.contratos.map((c) => (c.contratoId === contratoId ? cambio(c) : c)),
});

/** Paso 1: incluir (con su versión elegible más reciente) o quitar un contrato. */
export function alternarContrato(estado: EstadoAsistente, contrato: ContratoElegible): EstadoAsistente {
  if (estado.contratos.some((c) => c.contratoId === contrato.id)) {
    return { ...estado, contratos: estado.contratos.filter((c) => c.contratoId !== contrato.id) };
  }
  const version = contrato.versiones[0];
  if (!version) return estado;
  return { ...estado, contratos: [...estado.contratos, { contratoId: contrato.id, versionId: version.id, operaciones: [], guias: [] }] };
}

/**
 * Paso 1: otra versión del contrato. Las operaciones elegidas se vacían (son del índice de la otra versión) y se quitan
 * las guías que ya no aplican.
 */
export function cambiarVersion(estado: EstadoAsistente, contrato: ContratoElegible, versionId: string): EstadoAsistente {
  const version = contrato.versiones.find((v) => v.id === versionId);
  if (!version) return estado;
  return conContrato(estado, contrato.id, (c) =>
    c.versionId === versionId
      ? c
      : {
          ...c,
          versionId,
          operaciones: [],
          guias: c.guias.filter((g) => {
            const guia = contrato.guias.find((x) => x.id === g.guiaId);
            return !!guia && aplicaA(guia, version.version);
          }),
        },
  );
}

/** Paso 2: marcar o desmarcar una operación. */
export function alternarOperacion(estado: EstadoAsistente, contratoId: string, clave: string): EstadoAsistente {
  return conContrato(estado, contratoId, (c) => ({
    ...c,
    operaciones: c.operaciones.includes(clave) ? c.operaciones.filter((o) => o !== clave) : [...c.operaciones, clave].sort(),
  }));
}

/** Paso 2: «Seleccionar todo» sobre lo que se ve (con el filtro aplicado). */
export function seleccionarTodas(estado: EstadoAsistente, contratoId: string, claves: string[]): EstadoAsistente {
  return conContrato(estado, contratoId, (c) => ({ ...c, operaciones: [...new Set([...c.operaciones, ...claves])].sort() }));
}

/** Paso 2: «Ninguna» sobre lo que se ve: las elegidas que el filtro esconde se quedan. */
export function quitarTodas(estado: EstadoAsistente, contratoId: string, claves: string[]): EstadoAsistente {
  const fuera = new Set(claves);
  return conContrato(estado, contratoId, (c) => ({ ...c, operaciones: c.operaciones.filter((o) => !fuera.has(o)) }));
}

/** Paso 3: incluir o quitar una guía (con su revisión publicada). */
export function alternarGuia(estado: EstadoAsistente, contratoId: string, guia: GuiaElegible): EstadoAsistente {
  return conContrato(estado, contratoId, (c) => ({
    ...c,
    guias: c.guias.some((g) => g.guiaId === guia.id)
      ? c.guias.filter((g) => g.guiaId !== guia.id)
      : [...c.guias, { guiaId: guia.id, revision: guia.revisionPublicada }],
  }));
}

/** Publicar exige al menos un contrato con una operación (HU 4.1). */
export const tieneAlgoQueCompartir = (contratos: Array<{ operaciones: string[] }>): boolean => contratos.some((c) => c.operaciones.length > 0);

/** ¿La guía aplica a esa versión? Sin límites, a todas (como `ReglasGuias.AplicaA` del servidor). */
export function aplicaA(guia: Pick<GuiaElegible, 'aplicaDesde' | 'aplicaHasta'>, version: string): boolean {
  return (!guia.aplicaDesde || compararSemver(version, guia.aplicaDesde) >= 0) && (!guia.aplicaHasta || compararSemver(version, guia.aplicaHasta) <= 0);
}

/** Precedencia de SemVer 2.0 (núcleo numérico y prerelease por identificadores). */
export function compararSemver(a: string, b: string): number {
  const partir = (v: string) => {
    const [nucleo = '', pre] = v.split('+')[0]!.split(/-(.*)/s);
    return { nucleo: nucleo.split('.').map(Number), pre: pre ? pre.split('.') : [] };
  };
  const x = partir(a);
  const y = partir(b);
  for (let i = 0; i < 3; i++) {
    const d = (x.nucleo[i] ?? 0) - (y.nucleo[i] ?? 0);
    if (d !== 0) return Math.sign(d);
  }
  if (x.pre.length === 0 || y.pre.length === 0) return x.pre.length === y.pre.length ? 0 : x.pre.length === 0 ? 1 : -1;
  for (let i = 0; i < Math.max(x.pre.length, y.pre.length); i++) {
    const p = x.pre[i];
    const q = y.pre[i];
    if (p === undefined) return -1;
    if (q === undefined) return 1;
    const np = /^\d+$/.test(p);
    const nq = /^\d+$/.test(q);
    if (np && nq && Number(p) !== Number(q)) return Math.sign(Number(p) - Number(q));
    if (np !== nq) return np ? -1 : 1;
    if (!np && p !== q) return p < q ? -1 : 1;
  }
  return 0;
}
