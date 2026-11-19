import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { describe, expect, it } from 'vitest';

import { servidoresPermitidos, urlPermitida } from '../visor/servidores';
import { documentoAsync } from './asyncapi';
import { aCsv } from './csv';
import { leerSpec } from './documento';
import { ejemplosDe } from './ejemplos';
import { operacionesOpenApi } from './openapi';
import { leerProto } from './proto';
import { matricesDeObligatoriedad, tablasDeReferencia } from './referencia';

/** Los specs reales del legado ICT (ya saneados, ATLAS-ADR-0037) son las fixtures de la vista del contrato. */
const LEGADO = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../../../contracts/legado');
const legado = (archivo: string) => readFileSync(join(LEGADO, archivo), 'utf8');

describe('OpenAPI del legado ICT', () => {
  const transact = leerSpec(legado('external-transact.yaml'))!;

  it('lee el YAML y arma las operaciones con parámetros, cuerpo, respuestas y errores', () => {
    const operaciones = operacionesOpenApi(transact);
    expect(operaciones.length).toBeGreaterThan(10);
    const registrar = operaciones.find((o) => o.cuerpo?.campos.some((c) => c.nombre === 'transaction_type' && c.valores));
    expect(registrar).toBeDefined();
    expect(registrar!.requiereToken).toBe(true);
    const tipo = registrar!.cuerpo!.campos.find((c) => c.nombre === 'transaction_type')!;
    expect(tipo.obligatorio).toBe(true);
    expect(tipo.valores?.length).toBeGreaterThan(3);
    expect(registrar!.cuerpo!.campos.some((c) => c.restricciones.some((r) => r.startsWith('máx.')))).toBe(true);
    expect(operaciones.some((o) => o.errores.some((e) => e.codigo === '401'))).toBe(true);
  });

  it('saca la matriz de obligatoriedad por tipo de trámite', () => {
    const [matriz] = matricesDeObligatoriedad(transact);
    expect(matriz!.esquema).toBe('SolicitudRegistroTramite');
    expect(matriz!.propiedad).toBe('transaction_type');
    expect(matriz!.tipos.map((t) => t.clave)).toEqual(['traspaso_bilateral', 'traspaso_unilateral', 'matricula_inicial', 'matricula_leasing']);
    expect(matriz!.filas.length).toBeGreaterThan(50);
    const vin = matriz!.filas.find((f) => f.campo === 'vin')!;
    expect(vin.valores).toEqual({ traspaso_bilateral: 'opcional', traspaso_unilateral: 'opcional', matricula_inicial: 'obligatorio', matricula_leasing: 'obligatorio' });
    expect(vin.condicion).toMatch(/Matricula/);
  });

  it('saca la guía de adjuntos por tipo de trámite con «no aplica»', () => {
    const [matriz] = matricesDeObligatoriedad(leerSpec(legado('external-transactattach.yaml'))!);
    expect(matriz!.clase).toBe('documentos');
    const improntas = matriz!.filas.find((f) => f.detalle === 'Improntas')!;
    expect(improntas.valores.traspaso_unilateral).toBe('no_aplica');
    expect(improntas.valores.matricula_inicial).toBe('obligatorio');
  });

  it('arma las tablas de referencia con código y descripción', () => {
    const tablas = tablasDeReferencia(transact);
    const documento = tablas.find((t) => t.id === 'TipoDocumento')!;
    expect(documento.titulo).toBe('Tabla Tipo Documento');
    expect(documento.filas).toContainEqual({ codigo: 'CC', descripcion: 'Cedula' });
  });

  it('genera ejemplos con variables y sin valores reales en los cinco lenguajes', () => {
    const operacion = operacionesOpenApi(transact).find((o) => o.cuerpo && o.requiereToken)!;
    const ejemplos = ejemplosDe(operacion);
    expect(Object.keys(ejemplos)).toEqual(['curl', 'csharp', 'python', 'go', 'js']);
    for (const codigo of Object.values(ejemplos)) {
      expect(codigo).toContain('{{baseUrl}}');
      expect(codigo).toContain('$TOKEN');
      expect(codigo).not.toMatch(/eyJ[\w-]{10,}/);
      expect(codigo).not.toContain('base-url.invalid');
    }
    expect(ejemplos.curl).toMatch(/^curl /);
  });

  it('la consola solo apunta a QA o sandbox, con las variables resueltas', () => {
    expect(servidoresPermitidos(transact.servers)).toEqual([expect.objectContaining({ url: 'https://ict.qa.flitsas.online', ambiente: 'qa' })]);
    expect(
      servidoresPermitidos([
        { url: 'https://api.flitsas.com', 'x-flit-ambiente': 'pdn' },
        { url: 'https://api.qa.flitsas.online', 'x-flit-ambiente': 'pdn' },
        { url: 'https://api.flitsas.com' },
        { url: 'http://api.qa.flitsas.online' },
        { url: '/api' },
        { url: 'https://{host}', variables: { host: {} } },
        { url: 'https://sandbox.atlas.flitsas.com/rest/demo/1.0' },
      ]),
    ).toEqual([expect.objectContaining({ url: 'https://sandbox.atlas.flitsas.com/rest/demo/1.0', ambiente: 'sandbox' })]);
    expect(urlPermitida('https://ict.qa.flitsas.online/api/v1/transact', ['https://ict.qa.flitsas.online'])).toBe(true);
    expect(urlPermitida('https://ict.flitsas.com/api', ['https://ict.qa.flitsas.online'])).toBe(false);
    expect(urlPermitida('https://sandbox.atlas.flitsas.com/rest/otro', ['https://sandbox.atlas.flitsas.com/rest/demo'])).toBe(false);
  });

  it('los ocho specs del legado se leen y sus webhooks salen como operaciones', () => {
    for (const archivo of ['external-attachments', 'external-auth', 'external-log', 'external-transactattach', 'external-transactstatus', 'external-transactwebhook', 'external-valiqueryext']) {
      expect(operacionesOpenApi(leerSpec(legado(`${archivo}.yaml`))!).length, archivo).toBeGreaterThan(0);
    }
    const webhook = operacionesOpenApi(leerSpec(legado('external-transactwebhook.yaml'))!).find((o) => o.esWebhook)!;
    expect(webhook.clave).toBe('POST webhook:estadoTramite');
    expect(ejemplosDe(webhook).curl).toContain('{{urlWebhook}}');
  });
});

describe('AsyncAPI y proto', () => {
  it('lee un AsyncAPI 3 con canales, operaciones y mensajes', () => {
    const doc = documentoAsync(
      leerSpec(`asyncapi: 3.0.0
info: { title: Eventos Demo, version: 2.0.0 }
channels:
  radicados:
    address: tramites.radicado
    messages:
      radicado: { $ref: '#/components/messages/Radicado' }
operations:
  publicarRadicado:
    action: send
    channel: { $ref: '#/channels/radicados' }
components:
  messages:
    Radicado:
      payload:
        type: object
        required: [id]
        properties: { id: { type: string, description: Id del trámite } }
`)!,
    );
    expect(doc.canales).toEqual([{ id: 'radicados', direccion: 'tramites.radicado', descripcion: undefined, mensajes: ['Radicado'] }]);
    expect(doc.operaciones[0]).toMatchObject({ id: 'publicarRadicado', accion: 'send', canal: 'tramites.radicado', mensajes: ['Radicado'] });
    expect(doc.mensajes[0]!.campos[0]).toMatchObject({ nombre: 'id', obligatorio: true, descripcion: 'Id del trámite' });
  });

  it('lee un AsyncAPI 2 con publish y subscribe', () => {
    const doc = documentoAsync(
      leerSpec(`asyncapi: 2.6.0
info: { title: Demo, version: 1.0.0 }
channels:
  avisos:
    subscribe:
      operationId: recibirAviso
      message: { name: Aviso, payload: { type: object, properties: { texto: { type: string } } } }
`)!,
    );
    expect(doc.operaciones).toEqual([expect.objectContaining({ id: 'recibirAviso', accion: 'subscribe', canal: 'avisos' })]);
    expect(doc.mensajes[0]!.nombre).toBe('Aviso');
  });

  it('lee servicios, rpc y mensajes de un proto, también en una sola línea', () => {
    const doc = leerProto(`syntax = "proto3";
package flitsas_demo.tramites.v1;
// Trámites de prueba.
service TramitesService {
  rpc Obtener (ObtenerRequest) returns (Tramite); // Uno solo.
  rpc Seguir (ObtenerRequest) returns (stream Tramite);
}
message ObtenerRequest { string id = 1; comun.v1.Sobre sobre = 2; }
message Tramite {
  string id = 1;
  repeated string etiquetas = 2;
  map<string, string> datos = 3;
  enum Estado { BORRADOR = 0; RADICADO = 1; }
}
`);
    expect(doc.paquete).toBe('flitsas_demo.tramites.v1');
    expect(doc.servicios[0]!.comentario).toBe('Trámites de prueba.');
    expect(doc.servicios[0]!.rpcs).toEqual([
      expect.objectContaining({ nombre: 'Obtener', entrada: 'ObtenerRequest', salida: 'Tramite', flujoSalida: false, comentario: 'Uno solo.' }),
      expect.objectContaining({ nombre: 'Seguir', flujoSalida: true }),
    ]);
    expect(doc.mensajes.map((m) => m.nombre)).toEqual(['ObtenerRequest', 'Tramite', 'Tramite.Estado']);
    expect(doc.mensajes[0]!.campos.map((c) => c.nombre)).toEqual(['id', 'sobre']);
    expect(doc.mensajes[1]!.campos.map((c) => `${c.etiqueta ?? ''}${c.tipo}`)).toEqual(['string', 'repeatedstring', 'mapmap<string, string>']);
    expect(doc.mensajes[2]!.campos.map((c) => c.nombre)).toEqual(['BORRADOR', 'RADICADO']);
  });
});

describe('CSV', () => {
  it('lleva BOM, «;» y neutraliza fórmulas', () => {
    expect(aCsv(['Código', 'Descripción'], [['CC', 'Cédula; de ciudadanía'], ['=1+1', undefined]])).toBe('﻿Código;Descripción\r\nCC;"Cédula; de ciudadanía"\r\n\'=1+1;\r\n');
  });
});
