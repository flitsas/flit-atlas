import './app/fuentes.css';
import './app/estilos.css';

import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';

import { App } from './app/App';
import { aplicarTema } from './app/tema';

const raiz = document.getElementById('raiz');
if (!raiz) {
  throw new Error('No existe el elemento #raiz en index.html');
}

aplicarTema();
createRoot(raiz).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
