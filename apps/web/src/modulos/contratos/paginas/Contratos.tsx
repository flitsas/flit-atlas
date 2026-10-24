import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router';

import { CargandoPagina } from '../../../app/layout/Shell';
import { NoEncontrada } from '../../../app/paginas/Estados';

// Cada pantalla en su chunk: la lista no descarga la pantalla del contrato (ni el lector de YAML) hasta que se abre uno.
const Lista = lazy(() => import('./Lista'));
const Contrato = lazy(() => import('./Contrato'));

/** `/contratos` (lista), `/contratos/<id>` (pantalla del contrato, última versión) y `/contratos/<id>/v/<versión>`. */
export default function Contratos() {
  return (
    <Suspense fallback={<CargandoPagina />}>
      <Routes>
        <Route index element={<Lista />} />
        <Route path=":id" element={<Contrato />} />
        <Route path=":id/v/:version" element={<Contrato />} />
        <Route path="*" element={<NoEncontrada />} />
      </Routes>
    </Suspense>
  );
}
