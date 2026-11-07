#!/usr/bin/env node
// Prueba de humo del servidor MCP de LikeC4 (HU 6.9): lo levanta por stdio sobre un modelo ficticio, hace el saludo del protocolo, lista
// las herramientas y le pregunta por un elemento. Uso: node probar.mjs [carpeta con .c4]
import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const aqui = dirname(fileURLToPath(import.meta.url));
const carpeta = process.argv[2] ?? join(aqui, '..', 'arquitectura-gen', 'fixtures', 'modelo-demo');
const bin = join(aqui, 'node_modules', '.bin', process.platform === 'win32' ? 'likec4.cmd' : 'likec4');
const servidor = spawn(bin, ['mcp', '--stdio', carpeta], { stdio: ['pipe', 'pipe', 'ignore'], shell: process.platform === 'win32' });

const pendientes = new Map();
let resto = '';
servidor.stdout.on('data', (trozo) => {
  resto += trozo;
  let fin;
  while ((fin = resto.indexOf('\n')) >= 0) {
    const linea = resto.slice(0, fin).trim();
    resto = resto.slice(fin + 1);
    if (!linea.startsWith('{')) continue;
    const mensaje = JSON.parse(linea);
    pendientes.get(mensaje.id)?.(mensaje);
    pendientes.delete(mensaje.id);
  }
});

let siguiente = 1;
function pedir(method, params) {
  const id = siguiente++;
  return new Promise((resolver, rechazar) => {
    const espera = setTimeout(() => rechazar(new Error(`Sin respuesta a ${method} en 60 s`)), 60_000);
    pendientes.set(id, (r) => {
      clearTimeout(espera);
      if (r.error) rechazar(new Error(`${method}: ${r.error.message}`));
      else resolver(r.result);
    });
    servidor.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', id, method, params })}\n`);
  });
}

try {
  const saludo = await pedir('initialize', { protocolVersion: '2025-03-26', capabilities: {}, clientInfo: { name: 'prueba-atlas', version: '1' } });
  servidor.stdin.write(`${JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' })}\n`);
  console.log(`Servidor: ${saludo.serverInfo?.name} ${saludo.serverInfo?.version}`);
  const { tools } = await pedir('tools/list', {});
  console.log(`${tools.length} herramientas: ${tools.map((t) => t.name).join(', ')}`);
  for (const necesaria of ['search-element', 'read-element', 'find-relationship-paths']) {
    if (!tools.some((t) => t.name === necesaria)) throw new Error(`Falta la herramienta ${necesaria}`);
  }
  const resultado = await pedir('tools/call', { name: 'search-element', arguments: { search: 'Portal' } });
  const texto = JSON.stringify(resultado);
  if (!texto.includes('demo.web')) throw new Error(`search-element no encontró el elemento: ${texto.slice(0, 300)}`);
  console.log('search-element encontró demo.web. Prueba correcta.');
} catch (error) {
  console.error(error.message);
  process.exitCode = 1;
} finally {
  servidor.kill();
  // En Windows, cmd.exe puede dejar vivo al hijo: el proceso no debe quedarse esperando por él.
  setTimeout(() => process.exit(process.exitCode ?? 0), 200);
}
