import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router';

import { CargandoPagina } from '../../../app/layout/Shell';
import { NoEncontrada } from '../../../app/paginas/Estados';

// Cada pantalla en su chunk: el listado no descarga la ficha ni sus formularios hasta que se abren.
const Lista = lazy(() => import('./Lista'));
const Ficha = lazy(() => import('./Ficha'));

/** `/catalogo` (listado con búsqueda y filtros) y `/catalogo/<slug>` (ficha del aplicativo con sus pestañas). */
export default function Catalogo() {
  return (
    <Suspense fallback={<CargandoPagina />}>
      <Routes>
        <Route index element={<Lista />} />
        <Route path=":slug" element={<Ficha />} />
        <Route path="*" element={<NoEncontrada />} />
      </Routes>
    </Suspense>
  );
}
