#!/usr/bin/env node
// Íconos y pantallas de inicio (splash) de la PWA a partir de la marca replicada en src/marca/ (ATLAS-ADR-0020).
// Rasteriza los SVG con el Chromium de Playwright, que ya es dependencia de las pruebas: no hace falta sharp.
// Los PNG quedan versionados en public/iconos/; vuelve a correrlo solo si cambia la marca:
//   pnpm iconos
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { chromium } from '@playwright/test';
import { BellRing, Bot, CalendarDays, Search } from 'lucide-react';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';

const web = join(dirname(fileURLToPath(import.meta.url)), '..');
const marca = join(web, 'src', 'marca');
const salida = join(web, 'public', 'iconos');
const AZUL_FLIT = '#162744';

const svg = async (nombre) =>
  `data:image/svg+xml;base64,${Buffer.from(await readFile(join(marca, nombre))).toString('base64')}`;
const isotipo = await svg('isotipo.svg');
const isotipoBlanco = await svg('isotipo-blanco.svg');
const poppins = `data:font/woff2;base64,${(
  await readFile(join(web, 'node_modules', '@fontsource', 'poppins', 'files', 'poppins-latin-600-normal.woff2'))
).toString('base64')}`;
const glifo = (Icono) =>
  `data:image/svg+xml;base64,${Buffer.from(renderToStaticMarkup(createElement(Icono, { color: '#ffffff', strokeWidth: 2 }))).toString('base64')}`;

/** Lienzo de `ancho`×`alto` con fondo opcional e imágenes o texto centrados (tamaño como fracción del lado menor). */
function lienzo({ ancho, alto, fondo, radio = 0, capas }) {
  const lado = Math.min(ancho, alto);
  const contenido = capas
    .map(({ src, texto, fraccion }) =>
      texto
        ? `<div style="font:600 ${Math.round(fraccion * lado)}px Poppins;color:#fff;letter-spacing:-0.01em">${texto}</div>`
        : `<img src="${src}" style="width:${Math.round(fraccion * lado)}px;height:auto">`,
    )
    .join('');
  return `<!doctype html><html><head><style>@font-face{font-family:Poppins;font-weight:600;src:url(${poppins}) format('woff2')}</style></head>
  <body style="margin:0">
    <div style="width:${ancho}px;height:${alto}px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:${Math.round(lado * 0.05)}px;background:${fondo ?? 'transparent'};border-radius:${radio}px">${contenido}</div>
  </body></html>`;
}

const iconos = [
  // Ícono normal (purpose any): el isotipo FLIT sobre fondo transparente.
  { archivo: 'icono-192.png', ancho: 192, alto: 192, capas: [{ src: isotipo, fraccion: 0.92 }] },
  { archivo: 'icono-512.png', ancho: 512, alto: 512, capas: [{ src: isotipo, fraccion: 0.92 }] },
  // Maskable: fondo completo y el isotipo dentro de la zona segura (círculo del 80 %).
  { archivo: 'icono-maskable-192.png', ancho: 192, alto: 192, fondo: AZUL_FLIT, capas: [{ src: isotipo, fraccion: 0.6 }] },
  { archivo: 'icono-maskable-512.png', ancho: 512, alto: 512, fondo: AZUL_FLIT, capas: [{ src: isotipo, fraccion: 0.6 }] },
  // iOS no admite transparencia en el ícono de inicio.
  { archivo: 'apple-touch-icon-180.png', ancho: 180, alto: 180, fondo: AZUL_FLIT, capas: [{ src: isotipo, fraccion: 0.7 }] },
  // Insignia monocroma de las notificaciones en Android (solo cuenta el canal alfa).
  { archivo: 'insignia-96.png', ancho: 96, alto: 96, capas: [{ src: isotipoBlanco, fraccion: 0.86 }] },
  // Atajos del manifiesto (prompt maestro §11: Soporte, Buscar y Asistente).
  ...[
    ['atajo-soporte-96.png', CalendarDays],
    ['atajo-buscar-96.png', Search],
    ['atajo-asistente-96.png', Bot],
    ['atajo-notificaciones-96.png', BellRing],
  ].map(([archivo, Icono]) => ({
    archivo,
    ancho: 96,
    alto: 96,
    fondo: AZUL_FLIT,
    radio: 20,
    capas: [{ src: glifo(Icono), fraccion: 0.55 }],
  })),
];

// Pantallas de inicio de iOS (Android la arma sola con el manifiesto). Tamaños en píxeles físicos, vertical.
export const PANTALLAS_IOS = [
  { ancho: 750, alto: 1334, escala: 2 }, // iPhone SE 2.ª y 3.ª generación
  { ancho: 1170, alto: 2532, escala: 3 }, // iPhone 12 a 14
  { ancho: 1179, alto: 2556, escala: 3 }, // iPhone 14 Pro a 16
  { ancho: 1284, alto: 2778, escala: 3 }, // iPhone 12 a 14 Plus y Pro Max
  { ancho: 1290, alto: 2796, escala: 3 }, // iPhone 14 Pro Max a 16 Plus
  { ancho: 1640, alto: 2360, escala: 2 }, // iPad Air
];
const pantallas = PANTALLAS_IOS.map(({ ancho, alto }) => ({
  archivo: `pantalla-${ancho}x${alto}.png`,
  ancho,
  alto,
  fondo: AZUL_FLIT,
  capas: [
    { src: isotipo, fraccion: 0.24 },
    { texto: 'Flit Atlas', fraccion: 0.075 },
  ],
}));

await mkdir(salida, { recursive: true });
const navegador = await chromium.launch();
try {
  for (const imagen of [...iconos, ...pantallas]) {
    const pagina = await navegador.newPage({ viewport: { width: imagen.ancho, height: imagen.alto } });
    await pagina.setContent(lienzo(imagen), { waitUntil: 'load' });
    const png = await pagina.screenshot({ omitBackground: !imagen.fondo || imagen.radio > 0, type: 'png' });
    await writeFile(join(salida, imagen.archivo), png);
    await pagina.close();
    console.log(`public/iconos/${imagen.archivo}`);
  }
} finally {
  await navegador.close();
}
await writeFile(join(web, 'public', 'favicon.svg'), await readFile(join(marca, 'isotipo.svg')));
console.log('public/favicon.svg');
