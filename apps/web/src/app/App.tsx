import { type FormEvent, useCallback, useEffect, useState } from 'react';

import { entrarSimulado, obtenerProveedores, obtenerYo, type Proveedores, salir, urlEntrarMicrosoft, type Yo } from './api';

type Estado =
  | { tipo: 'cargando' }
  | { tipo: 'sin_sesion'; proveedores: Proveedores }
  | { tipo: 'con_sesion'; yo: Yo }
  | { tipo: 'error' };

const MENSAJES_ERROR: Record<string, string> = {
  cuenta_no_permitida: 'Esa cuenta no puede entrar a Atlas. Usa tu cuenta de FLIT.',
  inicio_fallido: 'No se pudo completar el inicio de sesión. Intenta de nuevo.',
};

/**
 * Ingreso de la HU 1.3: «Iniciar sesión con Microsoft» y, solo en local y dev, el proveedor simulado.
 * El shell móvil primero (barra inferior, módulos, tema) llega en la HU 1.6.
 */
export function App() {
  const [estado, setEstado] = useState<Estado>({ tipo: 'cargando' });
  const error = new URLSearchParams(window.location.search).get('error');

  const cargar = useCallback(async () => {
    try {
      const yo = await obtenerYo();
      setEstado(yo ? { tipo: 'con_sesion', yo } : { tipo: 'sin_sesion', proveedores: await obtenerProveedores() });
    } catch {
      setEstado({ tipo: 'error' });
    }
  }, []);

  useEffect(() => {
    void cargar();
  }, [cargar]);

  return (
    <main>
      <h1>Flit Atlas</h1>
      {error && MENSAJES_ERROR[error] && <p role="alert">{MENSAJES_ERROR[error]}</p>}
      {estado.tipo === 'cargando' && <p role="status">Cargando…</p>}
      {estado.tipo === 'error' && <p role="alert">No se pudo conectar con Atlas.</p>}
      {estado.tipo === 'sin_sesion' && <Ingreso proveedores={estado.proveedores} alEntrar={cargar} />}
      {estado.tipo === 'con_sesion' && <Sesion yo={estado.yo} alSalir={cargar} />}
    </main>
  );
}

function Ingreso({ proveedores, alEntrar }: { proveedores: Proveedores; alEntrar: () => Promise<void> }) {
  const [correo, setCorreo] = useState('');
  const [nombre, setNombre] = useState('');
  const [fallo, setFallo] = useState(false);

  async function enviar(evento: FormEvent) {
    evento.preventDefault();
    setFallo(false);
    try {
      await entrarSimulado(correo, nombre);
      await alEntrar();
    } catch {
      setFallo(true);
    }
  }

  return (
    <section aria-label="Iniciar sesión">
      {proveedores.microsoft && <a href={urlEntrarMicrosoft()}>Iniciar sesión con Microsoft</a>}
      {proveedores.simulado && (
        <form onSubmit={enviar} aria-label="Ingreso simulado (solo desarrollo)">
          <p>Ingreso simulado: solo existe en local y dev.</p>
          <label>
            Correo
            <input type="email" required value={correo} onChange={(e) => setCorreo(e.target.value)} />
          </label>
          <label>
            Nombre
            <input required value={nombre} onChange={(e) => setNombre(e.target.value)} />
          </label>
          <button type="submit">Entrar (simulado)</button>
          {fallo && <p role="alert">No se pudo entrar.</p>}
        </form>
      )}
      {!proveedores.microsoft && !proveedores.simulado && <p>No hay métodos de ingreso configurados.</p>}
    </section>
  );
}

function Sesion({ yo, alSalir }: { yo: Yo; alSalir: () => Promise<void> }) {
  async function cerrar() {
    await salir();
    await alSalir();
  }

  return (
    <section aria-label="Sesión">
      <p>Hola, {yo.nombre}.</p>
      <p>Roles: {yo.roles.length > 0 ? yo.roles.join(', ') : 'ninguno todavía'}</p>
      <button type="button" onClick={() => void cerrar()}>
        Cerrar sesión
      </button>
    </section>
  );
}
