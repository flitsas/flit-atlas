import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { BotonCompartir } from './Compartir';

/** HU 4.6: botón Compartir con la hoja nativa simulada y el respaldo (copiar, correo, Teams). */
const sesion = vi.hoisted(() => ({ tipoPrincipal: 'empleado' as 'empleado' | 'externo' }));

vi.mock('../sesion', () => ({
  useSesionOpcional: () => ({ yo: { tipoPrincipal: sesion.tipoPrincipal } }),
}));

const ENLACE = `${window.location.origin}/contratos/c1?pestana=guias`;

function simularShare(share: (datos: ShareData) => Promise<void>) {
  const espia = vi.fn(share);
  Object.defineProperty(navigator, 'share', { value: espia, configurable: true, writable: true });
  Object.defineProperty(navigator, 'canShare', { value: () => true, configurable: true, writable: true });
  return espia;
}

function montar() {
  return render(<BotonCompartir ruta="/contratos/c1?pestana=guias" titulo="Contrato Demo · versión 1.0.0" que="este contrato" />);
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'share');
  Reflect.deleteProperty(navigator, 'canShare');
  Reflect.deleteProperty(navigator, 'clipboard');
  sesion.tipoPrincipal = 'empleado';
});

describe('BotonCompartir', () => {
  it('con hoja nativa comparte el enlace interno en el mismo toque, sin hoja propia', async () => {
    const share = simularShare(async () => undefined);
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir' }));
    await waitFor(() => expect(share).toHaveBeenCalledWith({ title: 'Contrato Demo · versión 1.0.0', text: 'Contrato Demo · versión 1.0.0 · Flit Atlas', url: ENLACE }));
    expect(screen.queryByRole('dialog', { name: 'Compartir' })).not.toBeInTheDocument();
  });

  it('si la persona cancela (AbortError) no muestra error ni el respaldo', async () => {
    const share = simularShare(async () => Promise.reject(new DOMException('Cancelado', 'AbortError')));
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir' }));
    await waitFor(() => expect(share).toHaveBeenCalled());
    await new Promise((r) => setTimeout(r, 0));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('si la hoja nativa falla abre el respaldo con un aviso', async () => {
    simularShare(async () => Promise.reject(new TypeError('raro')));
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir' }));
    const hoja = await screen.findByRole('dialog', { name: 'Compartir' });
    expect(within(hoja).getByText(/No se pudo abrir la hoja de compartir/)).toBeInTheDocument();
  });

  it('sin hoja nativa: copiar con aviso, correo y Teams con el enlace interno', async () => {
    const writeText = vi.fn(async () => undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir' }));
    const hoja = await screen.findByRole('dialog', { name: 'Compartir' });
    expect(within(hoja).getByRole('textbox', { name: 'Enlace' })).toHaveValue(ENLACE);
    expect(within(hoja).getByText(/No es un enlace público/)).toBeInTheDocument();

    fireEvent.click(within(hoja).getByRole('button', { name: 'Copiar enlace' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(ENLACE));
    expect(await within(hoja).findByRole('status')).toHaveTextContent('Copiamos el enlace al portapapeles.');
    expect(within(hoja).getByRole('button', { name: 'Enlace copiado' })).toBeInTheDocument();

    const correo = within(hoja).getByRole('link', { name: 'Abrir en correo' }).getAttribute('href')!;
    expect(correo).toContain('mailto:?subject=');
    expect(decodeURIComponent(correo)).toContain(ENLACE);
    const teams = within(hoja).getByRole('link', { name: 'Abrir en Teams' });
    expect(new URL(teams.getAttribute('href')!).searchParams.get('href')).toBe(ENLACE);
    expect(teams).toHaveAttribute('rel', 'noopener noreferrer');
  });

  it('sin portapapeles avisa que se copie a mano; un externo no ve Teams', async () => {
    sesion.tipoPrincipal = 'externo';
    montar();
    fireEvent.click(screen.getByRole('button', { name: 'Compartir' }));
    const hoja = await screen.findByRole('dialog', { name: 'Compartir' });
    fireEvent.click(within(hoja).getByRole('button', { name: 'Copiar enlace' }));
    expect(await within(hoja).findByText(/selecciónalo en el campo y cópialo/)).toBeInTheDocument();
    expect(within(hoja).queryByRole('link', { name: 'Abrir en Teams' })).not.toBeInTheDocument();
    expect(within(hoja).getByRole('link', { name: 'Abrir en correo' })).toBeInTheDocument();
  });
});
