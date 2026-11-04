import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
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

/**
 * El contrato real de ICT de FLIT 2 enriquecido con el Excel (`contracts/ict/core-ict.enriquecido.v1.yaml`, ya saneado,
 * ATLAS-ADR-0037) es la fixture de la vista del contrato.
 */
const ICT = resolve(dirname(fileURLToPath(import.meta.url)), '../../../../../../contracts/ict/core-ict.enriquecido.v1.yaml');

describe('OpenAPI de ICT enriquecido', () => {
  const ict = leerSpec(readFileSync(ICT, 'utf8'))!;
  const matriz = (esquema: string) => matricesDeObligatoriedad(ict).find((m) => m.esquema === esquema)!;

  it('lee el YAML y arma las operaciones con parámetros, cuerpo, respuestas y errores', () => {
    const operaciones = operacionesOpenApi(ict);
    expect(operaciones.length).toBeGreaterThan(10);
    // El registro por lote (arreglo de `oneOf` por tipo de trámite) se aplana en los campos de la solicitud.
    const registrar = operaciones.find((o) => o.clave === 'POST /api/v1/external-transaction/register')!;
    expect(registrar.requiereToken).toBe(true);
    const tipo = registrar.cuerpo!.campos.find((c) => c.nombre === 'transaction_type')!;
    expect(tipo.valores?.length).toBeGreaterThan(3);
    expect(registrar.cuerpo!.campos.some((c) => c.restricciones.some((r) => r.startsWith('máx.')))).toBe(true);
    expect(registrar.errores.map((e) => e.codigo)).toEqual(expect.arrayContaining(['400', '401', '422']));
    expect(operaciones.some((o) => o.errores.some((e) => e.codigo === '401'))).toBe(true);
  });

  it('saca la matriz de obligatoriedad por tipo de trámite', () => {
    const registro = matriz('SolicitudRegistroTramite');
    expect(registro.propiedad).toBe('transaction_type');
    expect(registro.tipos.map((t) => t.clave)).toEqual(['traspaso_bilateral', 'traspaso_unilateral', 'matricula_inicial', 'matricula_leasing']);
    expect(registro.filas.length).toBeGreaterThan(50);
    const vin = registro.filas.find((f) => f.campo === 'vin')!;
    expect(vin.valores).toEqual({ traspaso_bilateral: 'opcional', traspaso_unilateral: 'opcional', matricula_inicial: 'obligatorio', matricula_leasing: 'obligatorio' });
    expect(vin.condicion).toMatch(/Matricula/);
  });

  it('saca la guía de adjuntos por tipo de trámite con «no aplica»', () => {
    const adjuntos = matriz('DocumentoAdjunto');
    expect(adjuntos.clase).toBe('documentos');
    const improntas = adjuntos.filas.find((f) => f.detalle === 'Improntas')!;
    expect(improntas.valores.traspaso_unilateral).toBe('no_aplica');
    expect(improntas.valores.matricula_inicial).toBe('obligatorio');
  });

  it('arma las tablas de referencia con código y descripción', () => {
    const tablas = tablasDeReferencia(ict);
    const documento = tablas.find((t) => t.id === 'TipoDocumento')!;
    expect(documento.titulo).toBe('Tabla Tipo Documento');
    expect(documento.filas).toContainEqual({ codigo: 'CC', descripcion: 'Cedula' });
  });

  it('genera ejemplos con variables y sin valores reales en los cinco lenguajes', () => {
    const operacion = operacionesOpenApi(ict).find((o) => o.cuerpo && o.requiereToken)!;
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
    // La URL base única de pdn (`https://{host}` con `x-flit-ambiente: pdn`) queda fuera.
    expect(servidoresPermitidos(ict.servers)).toEqual([expect.objectContaining({ url: 'https://ict.qa.flitsas.com', ambiente: 'qa' })]);
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
    expect(urlPermitida('https://ict.qa.flitsas.com/api/v1/external-transaction/register', ['https://ict.qa.flitsas.com'])).toBe(true);
    expect(urlPermitida('https://ict.flitsas.com/api', ['https://ict.qa.flitsas.com'])).toBe(false);
    expect(urlPermitida('https://sandbox.atlas.flitsas.com/rest/otro', ['https://sandbox.atlas.flitsas.com/rest/demo'])).toBe(false);
  });

  it('el webhook de estado sale como operación con su variable de URL', () => {
    const webhook = operacionesOpenApi(ict).find((o) => o.esWebhook)!;
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
