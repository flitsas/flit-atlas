import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router';

import { CargandoPagina } from '../../../app/layout/Shell';
import { NoEncontrada } from '../../../app/paginas/Estados';

// Cada pantalla en su chunk: la lista no descarga la pantalla del contrato (ni el lector de YAML) hasta que se abre uno.
const Lista = lazy(() => import('./Lista'));
const Contrato = lazy(() => import('./Contrato'));
const Consolidado = lazy(() => import('./Consolidado'));
const Guia = lazy(() => import('./Guia'));

/**
 * `/contratos` (lista), `/contratos/consolidado` (HU 3.4), `/contratos/<id>/guias/<guía>` (HU 3.7), `/contratos/<id>`
 * (pantalla del contrato, última versión) y `/contratos/<id>/v/<versión>`. El manifiesto (`modulo.ts`) también registra
 * el consolidado y la guía como rutas propias; aquí van declaradas antes de `:id` para que nunca las capture el contrato.
 */
export default function Contratos() {
  return (
    <Suspense fallback={<CargandoPagina />}>
      <Routes>
        <Route index element={<Lista />} />
        <Route path="consolidado" element={<Consolidado />} />
        <Route path=":contratoId/guias/:guiaId" element={<Guia />} />
        <Route path=":id" element={<Contrato />} />
        <Route path=":id/v/:version" element={<Contrato />} />
        <Route path="*" element={<NoEncontrada />} />
      </Routes>
    </Suspense>
  );
}
