---
id: ATLAS-ADR-0036
titulo: Diff de contratos en atlas-render — oasdiff como librería con la configuración de la CLI, caché LRU en memoria por par de SHA-256 y cliente tipado IRenderDiff en Plataforma
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-09, §5.1, §8.4 y §13, plan 00 §8.2 y §8.5, HU 3.5, ATLAS-ADR-0008, ATLAS-ADR-0010]
---

# ATLAS-ADR-0036 · Diff de contratos en atlas-render

## Contexto y problema

El plan 00 (§8.2, regla 6) pide «diff entre dos versiones cualesquiera, generado por oasdiff en el servidor y cacheado», y la HU 3.5 lo pone en `atlas-render` (Go) con un criterio de aceptación: **coincide con la CLI**. ATLAS-ADR-0010 ya eligió oasdiff, pero no fijó cómo se embebe, qué configuración usa, dónde vive la caché ni cómo lo pide `atlas-api`. Además las specs llegan de fuera (los repos que publican), así que el servicio debe protegerse de specs grandes, lentas o con referencias externas.

Hechos verificados el 23/10/2026: la última versión de `github.com/oasdiff/oasdiff` con al menos dos semanas es **v1.33.0** (16/09/2026; v1.34.0 y v1.34.1 son del 18 y 21/10), con licencia Apache-2.0, y es la misma que fija el job de contratos del CI. Trae los textos de los cambios en español (`--lang es`), pero la plantilla Markdown del changelog solo en inglés. `atlas-render` no usa Redis ni ninguna otra dependencia. Para AsyncAPI no hay un diff maduro en Go, y `buf breaking` (proto) no se ofrece como librería estable (sus paquetes Go son privados).

## Factores de decisión

- El mismo resultado en el servidor que en la CLI del CI (`oasdiff breaking` y `oasdiff changelog`).
- Sin dependencias nuevas de infraestructura para un resultado que se puede recalcular.
- Ningún contenido de las specs en los logs; nada de SSRF por `$ref` externas.
- Un contrato de llamada estable para el módulo de contratos (HU 3.6), que se pueda simular en pruebas.

## Opciones consideradas

1. oasdiff como librería en `atlas-render`, caché LRU en memoria por par de SHA-256, endpoint HTTP interno y cliente tipado en `atlas-api`.
2. Igual, pero con la caché en Redis.
3. Ejecutar el binario de la CLI desde `atlas-render` (o desde `atlas-api`).
4. Precalcular el diff al publicar y guardarlo en la base.

## Decisión

Opción elegida: **1**.

- **Endpoint** `POST /internal/v1/contratos/diff` en `atlas-render`: cuerpo JSON `{tipo, base, revision, idioma}` con las dos specs completas (YAML o JSON) y `idioma` `es` (por defecto) o `en`. Responde `cambios` (como `oasdiff changelog`, nivel info y superiores), `breaking` (como `oasdiff breaking`, error y warning, con el id de regla y el nivel), `resumen`, `changelog` en Markdown, los SHA-256 de las dos specs, `info.version` de cada una y el motor (`oasdiff v1.33.0`). Errores con `application/problem+json`: 400, 413 (cuerpo mayor que `ATLAS_RENDER_DIFF_MAX_BYTES`, 8 MiB por defecto), 415, 422 (spec inválida, con `lado`), 422 «diff no soportado para este tipo» (AsyncAPI y proto), 503 (sin turno libre) y 504 (más de `ATLAS_RENDER_DIFF_TIEMPO`, 15 s por defecto).
- **Misma configuración que la CLI por defecto**: `diff.NewConfig()`, `load.WithLowercaseHeaders()` (`--case-insensitive-headers`), `IncludeOrigin`, `checker.GetAllChecks()` con sus niveles por defecto y `formatters.NewChanges` para los textos. Única diferencia deliberada: **sin referencias externas** (equivale a `--allow-external-refs=false`), porque una `$ref` a una URL sería una puerta a SSRF.
- **Changelog en español**: los textos de cada cambio son los de oasdiff con `--lang es` (lo que no tenga traducción sale en inglés); los títulos y el resumen los pone Atlas, porque la plantilla de oasdiff es solo en inglés.
- **Caché LRU en memoria** con tope de entradas (`ATLAS_RENDER_DIFF_CACHE_ENTRADAS`, 256) y de bytes (`ATLAS_RENDER_DIFF_CACHE_MAX_BYTES`, 64 MiB). La clave es tipo + idioma + versión del motor + SHA-256 de la base + SHA-256 de la revisión (en ese orden). Guarda la respuesta ya serializada. No es estado del dominio: perderla al reiniciar o tener una por réplica solo cuesta recalcular, por eso no contradice la regla del §5.1 («ningún servicio guarda estado en memoria») ni justifica traer Redis a `atlas-render`. Si en pdn el diff se vuelve caliente entre réplicas, `atlas-api` puede sumar HybridCache (Redis) delante de `IRenderDiff` sin cambiar este contrato.
- **Concurrencia y tiempo**: como mucho `ATLAS_RENDER_DIFF_CONCURRENCIA` diffs a la vez (por defecto, los núcleos). Si vence el tiempo, la respuesta es 504 pero el cálculo termina y queda en caché, así el reintento (que hace el manejador de resiliencia de `atlas-api`) es inmediato.
- **Observabilidad sin contenido**: métricas Prometheus en `GET /metrics` (`atlas_render_diff_peticiones_total{tipo,resultado}`, `atlas_render_diff_duracion_segundos{cache}`, `atlas_render_diff_en_curso`, `atlas_render_diff_cache_entradas`, `atlas_render_diff_cache_bytes`) y un log por petición con tipo, idioma, prefijo de los hashes, tamaños, resultado, número de cambios y duración. Nunca el texto de las specs ni el error del analizador (que puede citarlas).
- **Cliente tipado** `IRenderDiff` en `Flit.Atlas.Plataforma.Render`: implementación HTTP (`ClienteRenderDiff`) hacia `http://render` por el descubrimiento de servicios de Aspire (configurable en `Plataforma:Render:Url`), con errores por motivo (`NoSoportado`, `SpecInvalida`, `Rechazado`, `NoDisponible`), y `RenderDiffSimulado` para las pruebas de los módulos. No hay endpoint público de diff en `atlas-api` en esta HU: llega con la pantalla de contrato (HU 3.6, `GET /contratos/{id}/diff?desde=&hasta=` del plan 00 §8.5).
- **AsyncAPI y proto**: pendientes. El endpoint responde 422 «diff no soportado para este tipo». ATLAS-ADR-0010 prevé un diff estructural propio para AsyncAPI y `buf breaking` para proto; se decidirá en otra HU (probablemente con el binario de `buf` en CI y en la imagen).
- **Criterio de aceptación**: `services/render/internal/diferencias/cli_test.go` compila la CLI del mismo módulo fijado en `go.mod` (directiva `tool`, sin instalar nada en el CI) y compara, campo por campo y en orden, la salida JSON de `oasdiff breaking` y `oasdiff changelog` con la del endpoint sobre las fixtures de `services/render/testdata/diff/openapi/`, en español y en inglés. También compara contra las funciones públicas de `checker`. Con `OASDIFF_CLI=<ruta>` usa un binario publicado (exige la misma versión).

## Consecuencias

### Positivas

- El diff del servidor es el de la CLI del CI, comprobado en cada `go test`.
- Sin infraestructura nueva; el binario de producción no incluye la CLI (la directiva `tool` solo afecta al módulo).
- El módulo de contratos tiene un contrato tipado y un doble para pruebas desde ya.

### Negativas

- La caché es por réplica y se pierde al reiniciar.
- Las dos specs viajan completas en cada petición (hasta 8 MiB); si pesa, el módulo de contratos puede enviar solo hashes y que `atlas-render` lea las specs del almacenamiento de objetos (cambio futuro).
- El endpoint es interno pero hoy no exige token de servicio: depende de la política de red del clúster. El token firmado por OpenIddict de ATLAS-ADR-0008 queda pendiente para todas las llamadas de `atlas-api` a los servicios internos.
- Al subir la versión de oasdiff hay que confirmar que siga existiendo la versión con al menos dos semanas y que el CI (job de contratos y `atlas-publicar`) use la misma; la prueba lo detecta si `OASDIFF_CLI` apunta al binario del CI.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Caché en Redis | `atlas-render` no usa Redis y el resultado es recalculable y determinista; agregaría una dependencia, credenciales y una sonda de salud por poco beneficio |
| Ejecutar el binario de la CLI | Un proceso por petición, parseo de texto y otra pieza en la imagen; la librería da lo mismo con las mismas funciones |
| Precalcular al publicar | El plan pide diff entre dos versiones cualesquiera, no solo entre consecutivas; se puede sumar como optimización (calentar la caché con el par contra pdn) |

## Más información

- Pendientes: diff de AsyncAPI y proto; token de servicio para las llamadas internas; HybridCache en `atlas-api` si hace falta compartir la caché entre réplicas; endpoint público `GET /contratos/{id}/diff` en la HU 3.6.
- Correr la comparación con la CLI localmente: ver `services/render/README.md`.
