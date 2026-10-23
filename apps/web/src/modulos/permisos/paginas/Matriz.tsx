import { Check, Minus } from 'lucide-react';
import { useId, useMemo, useState } from 'react';

import { Icono } from '../../../app/ui/Icono';
import { cn, Insignia, Tarjeta } from '../../../app/ui/Tarjeta';
import { type MatrizPermisos, type ModuloMatriz, type RolMatriz, useMatriz } from '../api';
import { type Grupo, permisosDelRol, rolesDelGrupo } from '../matriz';
import { moduloPermisos } from '../modulo';

const GRUPOS: { valor: Grupo; etiqueta: string }[] = [
  { valor: 'interno', etiqueta: 'Internos' },
  { valor: 'externo', etiqueta: 'Externos' },
  { valor: 'todos', etiqueta: 'Todos' },
];

const TIPOS: Record<RolMatriz['tipo'], string> = { interno: 'Interno', externo: 'Externo', implicito: 'Implícito' };

type Vista = 'modulo' | 'rol';

/**
 * Matriz rol × permiso (CA de la HU 1.4: «visible en Atlas»). En escritorio, una tabla por módulo con los roles como
 * columnas; en el móvil, tarjetas por módulo o por rol, sin scroll horizontal a 360 px. Los datos salen del código
 * (`CatalogoPermisos`, ATLAS-ADR-0023): esta pantalla solo los muestra.
 */
export default function Matriz() {
  const matriz = useMatriz();
  const [grupo, setGrupo] = useState<Grupo>('interno');
  const [vista, setVista] = useState<Vista>('modulo');

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-4">
      <header className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-2xl bg-degradado-flit text-[#0b1220]">
          <Icono nombre={moduloPermisos.icono} className="size-6" />
        </span>
        <div className="min-w-0">
          <h1 className="text-2xl font-semibold">Roles y permisos</h1>
          <p className="max-w-3xl text-sm text-atlas-texto-suave">
            Qué puede hacer cada rol en cada módulo. La matriz sale del código de Atlas y cambia solo con un despliegue.
            Todo empleado tiene además el rol Lector, y Superadministrador recibe todos los permisos.
          </p>
        </div>
      </header>

      {matriz.isPending && <div role="status" aria-label="Cargando la matriz" className="h-40 animate-pulse rounded-2xl bg-atlas-activo" />}
      {matriz.isError && (
        <p role="alert" className="rounded-xl border border-border bg-card p-4 text-sm">
          No pudimos cargar la matriz de permisos. Revisa tu conexión e inténtalo de nuevo.
        </p>
      )}

      {matriz.data && (
        <>
          <div className="flex flex-wrap items-end gap-x-6 gap-y-3">
            <Opciones
              leyenda="Roles"
              valor={grupo}
              opciones={GRUPOS.map((g) => ({
                ...g,
                etiqueta: `${g.etiqueta} (${rolesDelGrupo(matriz.data.roles, g.valor).length})`,
              }))}
              alCambiar={setGrupo}
            />
            <Opciones
              leyenda="Ver por"
              valor={vista}
              opciones={[
                { valor: 'modulo', etiqueta: 'Módulo' },
                { valor: 'rol', etiqueta: 'Rol' },
              ]}
              alCambiar={setVista}
              className="lg:hidden"
            />
          </div>

          <div className="hidden flex-col gap-4 lg:flex">
            {matriz.data.modulos.map((modulo) => (
              <TablaModulo key={modulo.id} modulo={modulo} roles={rolesDelGrupo(matriz.data.roles, grupo)} />
            ))}
          </div>

          <div className="flex flex-col gap-3 lg:hidden">
            {vista === 'modulo' ? (
              matriz.data.modulos.map((modulo) => (
                <TarjetaModulo key={modulo.id} modulo={modulo} roles={rolesDelGrupo(matriz.data.roles, grupo)} />
              ))
            ) : (
              <VistaPorRol matriz={matriz.data} roles={rolesDelGrupo(matriz.data.roles, grupo)} />
            )}
          </div>
        </>
      )}
    </div>
  );
}

function Opciones<T extends string>({
  leyenda,
  valor,
  opciones,
  alCambiar,
  className,
}: {
  leyenda: string;
  valor: T;
  opciones: { valor: T; etiqueta: string }[];
  alCambiar: (valor: T) => void;
  className?: string;
}) {
  const nombre = useId();
  return (
    <fieldset className={className}>
      <legend className="mb-1 text-sm font-medium">{leyenda}</legend>
      <div className="flex flex-wrap gap-2">
        {opciones.map((o) => (
          <label
            key={o.valor}
            className={cn(
              'inline-flex min-h-toque cursor-pointer items-center gap-2 rounded-xl border px-3 text-sm',
              o.valor === valor ? 'border-atlas-tinta bg-atlas-activo font-medium' : 'border-border bg-card',
            )}
          >
            <input
              type="radio"
              name={nombre}
              value={o.valor}
              checked={o.valor === valor}
              onChange={() => alCambiar(o.valor)}
              className="size-4 accent-[var(--flit-brand-ink)]"
            />
            {o.etiqueta}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

function Concedido({ si }: { si: boolean }) {
  return si ? (
    <>
      <Check className="mx-auto size-4.5 text-atlas-exito" aria-hidden="true" />
      <span className="sr-only">Sí</span>
    </>
  ) : (
    <>
      <Minus className="mx-auto size-4 text-atlas-texto-suave opacity-50" aria-hidden="true" />
      <span className="sr-only">No</span>
    </>
  );
}

/** Escritorio: tabla del módulo con los roles del grupo como columnas. Con «Todos» puede desplazarse dentro de su caja. */
function TablaModulo({ modulo, roles }: { modulo: ModuloMatriz; roles: RolMatriz[] }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="rounded-2xl border border-border bg-card shadow-tarjeta">
      <h2 id={id} className="flex items-baseline gap-2 px-5 pt-4 text-base font-semibold">
        {modulo.nombre}
        <code className="text-xs font-normal text-atlas-texto-suave">{modulo.id}</code>
      </h2>
      {/* Región enfocable: con muchos roles la tabla se desplaza aquí dentro, nunca la página. «relative» hace que los
          textos sr-only (posición absoluta) de las celdas también queden recortados por esta caja. */}
      <div role="region" aria-labelledby={id} tabIndex={0} className="relative overflow-x-auto px-2 pb-3 outline-none focus-visible:ring-2 focus-visible:ring-atlas-tinta">
        <table className="w-full border-separate border-spacing-0 text-sm">
          <caption className="sr-only">Permisos de {modulo.nombre} por rol</caption>
          <thead>
            <tr>
              <th scope="col" className="min-w-64 px-3 py-2 text-left align-bottom font-medium text-atlas-texto-suave">
                Permiso
              </th>
              {roles.map((r) => (
                <th key={r.codigo} scope="col" className="w-20 px-1 py-2 text-center align-bottom text-xs leading-tight font-medium">
                  <abbr title={r.descripcion} className="no-underline">
                    {r.nombre}
                  </abbr>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {modulo.permisos.map((p) => (
              <tr key={p.codigo} className="hover:bg-atlas-activo">
                <th scope="row" className="border-t border-border px-3 py-2 text-left align-top font-normal">
                  <code className="text-xs font-medium break-all">{p.codigo}</code>
                  <span className="mt-0.5 block text-atlas-texto-suave">{p.descripcion}</span>
                </th>
                {roles.map((r) => (
                  <td key={r.codigo} className="border-t border-border px-1 py-2 text-center align-middle">
                    <Concedido si={p.roles.includes(r.codigo)} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

/** Móvil, por módulo: cada permiso con los roles del grupo que lo conceden. */
function TarjetaModulo({ modulo, roles }: { modulo: ModuloMatriz; roles: RolMatriz[] }) {
  return (
    <Tarjeta titulo={modulo.nombre}>
      <ul className="flex flex-col divide-y divide-border">
        {modulo.permisos.map((p) => {
          const con = roles.filter((r) => p.roles.includes(r.codigo));
          return (
            <li key={p.codigo} className="py-3 first:pt-0 last:pb-0">
              <code className="text-xs font-medium break-all">{p.codigo}</code>
              <p className="mt-0.5 text-sm text-atlas-texto-suave">{p.descripcion}</p>
              {con.length > 0 ? (
                <ul aria-label={`Roles con ${p.codigo}`} className="mt-2 flex flex-wrap gap-1.5">
                  {con.map((r) => (
                    <li key={r.codigo}>
                      <Insignia tono={r.tipo === 'externo' ? 'neutral' : 'info'}>{r.nombre}</Insignia>
                    </li>
                  ))}
                </ul>
              ) : (
                <p className="mt-2 text-xs text-atlas-texto-suave">Ningún rol de este grupo.</p>
              )}
            </li>
          );
        })}
      </ul>
    </Tarjeta>
  );
}

/** Móvil, por rol: se elige un rol y se ven su descripción y sus permisos por módulo. */
function VistaPorRol({ matriz, roles }: { matriz: MatrizPermisos; roles: RolMatriz[] }) {
  const id = useId();
  const [elegido, setElegido] = useState<string>(roles[0]?.codigo ?? '');
  const rol = roles.find((r) => r.codigo === elegido) ?? roles[0];
  const modulos = useMemo(() => (rol ? permisosDelRol(matriz, rol.codigo) : []), [matriz, rol]);
  if (!rol) return null;
  const total = modulos.reduce((suma, m) => suma + m.permisos.length, 0);

  return (
    <>
      {/* Opciones en lugar de <select>: WebKit aplica estilos en línea al crear un <select> y la CSP lo reporta. */}
      <Opciones leyenda="Rol" valor={rol.codigo} opciones={roles.map((r) => ({ valor: r.codigo, etiqueta: r.nombre }))} alCambiar={setElegido} />

      <section aria-labelledby={`${id}-rol`} className="rounded-2xl border border-border bg-card p-4 shadow-tarjeta">
        <h2 id={`${id}-rol`} className="text-base font-semibold">
          {rol.nombre}
        </h2>
        <p className="mt-2 flex flex-wrap items-center gap-2">
          <Insignia tono={rol.tipo === 'externo' ? 'neutral' : 'info'}>{TIPOS[rol.tipo]}</Insignia>
          {!rol.asignable && <Insignia tono="neutral">Por propiedad, no se asigna</Insignia>}
          <code className="text-xs text-atlas-texto-suave break-all">{rol.codigo}</code>
        </p>
        <p className="mt-2 text-sm">{rol.descripcion}</p>
        <p className="mt-2 text-sm font-medium" aria-live="polite">
          {total === 1 ? '1 permiso' : `${total} permisos`}
        </p>
      </section>

      {modulos.map((m) => (
        <Tarjeta key={m.id} titulo={m.nombre}>
          <ul className="flex flex-col divide-y divide-border">
            {m.permisos.map((p) => (
              <li key={p.codigo} className="py-2.5 first:pt-0 last:pb-0">
                <code className="text-xs font-medium break-all">{p.codigo}</code>
                <p className="mt-0.5 text-sm text-atlas-texto-suave">{p.descripcion}</p>
              </li>
            ))}
          </ul>
        </Tarjeta>
      ))}
    </>
  );
}
