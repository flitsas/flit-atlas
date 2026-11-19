# tests/carga · Prueba de carga de Atlas

Prueba de la HU 4.10 (ATLAS-ADR-0048) con [k6](https://github.com/grafana/k6) **2.3.0** (binario de Go, publicado el
21/09/2026): `atlas.js` simula **20 personas navegando** (inicio, catálogo, contratos y portal, con pausas de 1 a 3 s) y,
desde el minuto 1, **2 exportaciones simultáneas** de un paquete en los siete formatos, por la cola y con atlas-docs.

## Umbrales

| Métrica | Umbral |
|---|---|
| p95 de las lecturas (`http_req_duration{tipo:lectura}`) | < 500 ms |
| p95 de las lecturas antes de las exportaciones (`fase:base`) y durante ellas (`fase:con_exportaciones`) | < 500 ms cada una: «sin degradar» |
| Lecturas con error (`http_req_failed{tipo:lectura}`, todo lo que no es 2xx o 3xx) | < 1 % |
| Exportaciones que quedan `lista` (`atlas_exportaciones_ok`) | 100 % |
| Checks (cada lectura 200; la exportación 202 y su URL firmada) | > 99 % |

Si un umbral no se cumple, k6 termina con un código distinto de cero.

## Instalar k6

Descarga el binario de la [versión 2.3.0](https://github.com/grafana/k6/releases/tag/v2.3.0) para tu sistema y verifica
su SHA-256 contra `k6-v2.3.0-checksums.txt` de la misma página. No hace falta instalarlo en el PATH: las pruebas reciben la
ruta.

## Local, reproducible (recomendado)

Una prueba de xUnit levanta todo con Docker (como `ExportacionesPuntaAPuntaTests`): Postgres, Redis, RabbitMQ, RustFS,
atlas-docs construido desde su Dockerfile y atlas-api en Kestrel con su outbox y su cola; siembra el contrato ICT
enriquecido en un paquete publicado y compartido con «Organismo Demo», 15 empleados (`atlas.desarrollador`) y 5 externos
(`ext.integrador`), abre sus sesiones y corre `atlas.js` con los límites de tasa de siempre. Sin `ATLAS_CARGA_K6`, la
prueba se omite (así no corre en el CI).

```bash
ATLAS_CARGA_K6=/ruta/a/k6 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests \
  --filter-class Flit.Atlas.Api.Tests.Carga.PruebaCargaTests
```

La prueba escribe el resumen (`resumen.json`), la salida de k6 (`salida.txt`) y los datos de la corrida (sesiones de
prueba e ids, `datos.json`) en una carpeta temporal `atlas-carga-*` que indica al terminar. `ATLAS_CARGA_DURACION`
cambia la duración de la navegación (3 minutos por defecto).

Cada persona simulada manda su propia IP de documentación (`198.51.100.x`) en `X-Forwarded-For`
(`ATLAS_CARGA_IP_POR_VU=1`): atlas-api de la prueba confía en el proxy de loopback, y así los límites por IP de la
identidad ven 20 clientes y no uno.

## Contra el AppHost de Aspire

Con `dotnet run --project aspire/Flit.Atlas.AppHost`, el proveedor simulado abre las sesiones (empleados con el rol de
administración). El ambiente local no trae contratos publicados (llegan por la CI): sin `ATLAS_PAQUETE_ID`, apaga las
exportaciones con `ATLAS_CARGA_EXPORTACIONES=0`; las pantallas de contratos solo piden lo que encuentren.

```bash
ATLAS_URL=https://localhost:4032 ATLAS_CARGA_TLS_INSEGURO=1 ATLAS_CARGA_SIMULADO=20 ATLAS_CARGA_EXPORTACIONES=0 \
  k6 run tests/carga/atlas.js
```

Todas las peticiones salen de tu IP: el ingreso simulado tiene un límite de 20 por minuto e IP en su configuración por
defecto (el AppHost lo sube a 120).

## Contra qa

En qa no hay proveedor simulado: las sesiones son de personas reales de prueba, que entran por la PWA, y su cookie
`__Host-atlas` se copia de las herramientas del navegador. **Nunca** las guardes en el repo, en un archivo versionado ni
en un mensaje de commit; ciérralas («Cerrar sesión» o «Dispositivos conectados») al terminar.

| Variable | Qué |
|---|---|
| `ATLAS_URL` | `https://qa.atlas.flitsas.com` |
| `ATLAS_COOKIES_INTERNOS` | Cookies `__Host-atlas=…` de empleados, separadas por comas (con `atlas.contratos.ver_internos` y `atlas.exportaciones.solicitar` para exportar) |
| `ATLAS_COOKIES_EXTERNOS` | Cookies de personas externas de una organización de prueba con un paquete compartido (opcional) |
| `ATLAS_PAQUETE_ID` | Paquete publicado que exportan las dos exportaciones |
| `ATLAS_CARGA_VUS`, `ATLAS_CARGA_DURACION`, `ATLAS_CARGA_INICIO_EXPORTAR` | 20, `3m` y 60 s por defecto |

```bash
export ATLAS_URL=https://qa.atlas.flitsas.com
export ATLAS_COOKIES_INTERNOS="$(cat ~/atlas-carga/internos.txt)"   # fuera del repo
export ATLAS_PAQUETE_ID=<id del paquete de prueba>
k6 run tests/carga/atlas.js
```

Con pocas sesiones, varias personas simuladas comparten una (la rueda reparte las 20 entre las que haya): cuida los
límites por persona (600 por minuto para empleados, 240 para externos) y por organización en el portal (600). Todas
las peticiones salen de tu IP: las rutas de identidad (`/api/v1/plataforma/yo` cuenta) tienen 120 por minuto e IP. Avisa
en el canal de operación antes de correrla y no la corras contra pdn.

## Resultados de referencia

Corrida local del 24/10/2026 con `PruebaCargaTests` (portátil de desarrollo con Docker Desktop: atlas-api, las
dependencias, atlas-docs y k6 en la misma máquina; 3 minutos; exportaciones desde el segundo 60):

| Métrica | Resultado | Umbral |
|---|---|---|
| Peticiones | 6.501 (34,7 por segundo), 627 recorridos | — |
| p95 de las lecturas | 134,7 ms (mediana 13,1 ms, p99 320,7 ms) | < 500 ms |
| p95 antes de las exportaciones | 28,5 ms | < 500 ms |
| p95 con las exportaciones en curso | 176,1 ms (máximo 614,7 ms) | < 500 ms |
| Lecturas con error | 0 de 6.486 (0 %) | < 1 % |
| Exportaciones listas (siete formatos del contrato ICT) | 2 de 2, en 8,6 s y 14,6 s | 100 % |
| Checks | 6.490 de 6.490 | > 99 % |

Las exportaciones suben el p95 de las lecturas (atlas-docs y la API comparten la máquina) sin acercarlo al umbral. Una
corrida anterior, con la base recién creada, dio 171,9 ms de p95 antes de las exportaciones (el primer minuto calienta
las cachés del portal y de EF Core): mira las dos fases, no solo el total.
