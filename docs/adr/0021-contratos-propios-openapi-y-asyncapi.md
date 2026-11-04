---
id: ATLAS-ADR-0021
titulo: Contratos propios de Atlas — OpenAPI generado desde el código y AsyncAPI atado al catálogo de eventos
estado: propuesto
fecha: 2026-10-22
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-09, §5.2, §5.3 y §13, F1 HU 1.7, ATLAS-ADR-0008, ATLAS-ADR-0010, ATLAS-ADR-0018, ATLAS-ADR-0019]
---

# ATLAS-ADR-0021 · Contratos propios de Atlas — OpenAPI generado desde el código y AsyncAPI atado al catálogo de eventos

## Contexto y problema

La HU 1.7 pide `contracts/openapi/atlas.v1.yaml` (OpenAPI 3.1) generado desde el código, `contracts/asyncapi/atlas-events.v1.yaml` con los eventos del exchange `flit.atlas`, y lint con vacuum en CI (ATLAS-ADR-0010). Hay que decidir:

- Cómo se genera el OpenAPI y cómo se evita que el archivo versionado se desactualice cuando un PR cambia un endpoint.
- Si el documento OpenAPI se sirve en tiempo de ejecución en qa y pdn.
- Qué hacer con el endpoint del proveedor simulado (`POST /api/v1/identidad/simulado/entrar`), que solo existe en local y dev.
- Cómo se describe la seguridad del BFF (cookie `__Host-atlas` y cabecera `X-Atlas-Csrf`, ATLAS-ADR-0008).
- Cómo se escribe el AsyncAPI, si no hay un generador maduro para .NET, sin que se aleje de lo que el código publica.
- Qué versión de vacuum y qué reglas usar.

Hechos verificados el 22/10/2026:

- `Microsoft.AspNetCore.OpenApi` 10.0.13 genera OpenAPI 3.1 y sirve YAML. Se fija en 10.0.13, como el resto del conjunto ASP.NET Core 10.0.x (parche de seguridad del 13/10/2026, excepción ya anotada en `Directory.Packages.props`).
- La generación en build (`Microsoft.Extensions.ApiDescription.Server`) ejecuta el `Program` de atlas-api: `ConfiguracionRequerida` exige cadenas de conexión y ambiente, y el publicador de la outbox arranca. Habría que darle configuración falsa al build o ramificar el arranque.
- Atlas aún no publica ningún evento desde código de producción: la outbox existe (ATLAS-ADR-0018), pero los módulos que publican llegan desde F2.
- vacuum v0.32.0 (07/10/2026, la más nueva con dos semanas de publicada) lint OpenAPI 3.1 y AsyncAPI 3.0. Un ruleset que extiende los dos conjuntos aplica solo las reglas de OpenAPI, la regla `asyncapi-servers` falla con cualquier documento y `asyncapi-3-tags` busca `tags` en la raíz, donde AsyncAPI 3.0 no los tiene.
- oasdiff v1.33.0 (01/10/2026) entiende OpenAPI 3.1.

## Factores de decisión

- El contrato versionado debe ser exactamente lo que el código expone y un PR que cambie un endpoint sin actualizarlo debe fallar.
- Nada de configuración falsa ni ramas especiales en el arranque de producción.
- No exponer más superficie de la necesaria en qa y pdn.
- Que el AsyncAPI no prometa eventos que no existen ni omita los que sí.
- Herramientas Go fijadas y verificadas por checksum (ATLAS-ADR-0010).

## Opciones consideradas

1. **OpenAPI: prueba de instantánea** en `Flit.Atlas.Api.Tests` que levanta atlas-api en memoria (`WebApplicationFactory`), pide `/openapi/v1.yaml` y lo compara con el archivo versionado; con `ATLAS_ACTUALIZAR_CONTRATOS=1` lo reescribe.
2. **OpenAPI: generación en build** con `Microsoft.Extensions.ApiDescription.Server` y `git diff --exit-code` en CI.
3. **AsyncAPI escrito a mano** y atado al código por un catálogo tipado (`TipoEvento`) más pruebas.
4. **AsyncAPI generado** (Saunter, Neuroglia.AsyncApi u otro).

## Decisión

Opciones elegidas: **1** y **3**.

### OpenAPI

- Generado con `Microsoft.AspNetCore.OpenApi` (`AddOpenApi("v1")`, OpenAPI 3.1) desde los endpoints, con transformadores en `Flit.Atlas.Api/ContratoOpenApi.cs`:
  - `info`, `tags` por módulo (`Identidad`, `Plataforma`, y uno por módulo funcional cuando lleguen) y `servers` con los hosts públicos de ATLAS-ADR-0019 (`https://atlas.flitsas.com` y `https://qa.atlas.flitsas.com`). Nunca `localhost` ni hosts internos.
  - Esquemas de seguridad `sesionAtlas` (`apiKey` en la cookie `__Host-atlas`) y `csrfAtlas` (`apiKey` en la cabecera `X-Atlas-Csrf`). Se deducen de la metadata de cada endpoint: los que piden autorización exigen la cookie y documentan 401; los que cambian estado (POST, PUT, PATCH, DELETE) exigen la cabecera y documentan 403. Las dos juntas son un solo requisito (AND).
  - Cada endpoint declara `WithName` (operationId en PascalCase y en español), `WithTags`, `WithSummary`, `WithDescription` y sus códigos (`Produces`); los esquemas y parámetros llevan `[Description]`.
- `contracts/openapi/atlas.v1.yaml` es la salida de ese generador, normalizada a LF. `ContratoOpenApiTests` falla si el archivo y el código no coinciden. Se regenera con:

  ```bash
  ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests
  ```

- El documento en tiempo de ejecución (`GET /openapi/v1.yaml`) **solo se sirve en local y dev**. En qa y pdn la ruta no existe (una prueba lo verifica): el contrato vive en el repo y se publicará por el módulo de contratos de Atlas (F3) con sus permisos.
- El **proveedor simulado no forma parte del contrato** (`ExcludeFromDescription`): no existe en qa ni en pdn (HU 1.3), y publicarlo invitaría a integrarse con algo que no está en los ambientes reales. Se documenta en `services/api/README.md`.

### AsyncAPI

- **AsyncAPI 3.0.0**, escrito a mano en `contracts/asyncapi/atlas-events.v1.yaml`: servidor RabbitMQ (sin host real, que lo da cada ambiente), canal `flit.atlas` (exchange topic durable, llave de ruteo = `type`), operación `publicarEvento`, mensaje `SobreEvento` con el esquema del sobre de ATLAS-ADR-0018 y, por cada evento, un mensaje con `x-atlas-tipo` y `x-atlas-version`. Se queda en 3.0 y no en 3.1 hasta verificar el soporte del visor de la PWA y de los consumidores en Go y Python.
- En el código, cada evento publicado es un campo `static readonly TipoEvento` (`atlas.<modulo>.<evento>` y versión) y se encola con la sobrecarga `EncolarEvento(TipoEvento, datos, organizacionId)`. Es el catálogo `EventosPublicados` que pedirá `IModuloAtlas` (ATLAS-ADR-0016).
- `ContratoAsyncApiTests` verifica que (a) cada `TipoEvento` de los ensamblados de Atlas está en el AsyncAPI y cada evento del AsyncAPI está en el código, (b) en `services/api/src` toda llamada a `EncolarEvento` usa un campo del catálogo y no un texto suelto, y (c) los campos requeridos de `SobreEvento` en el AsyncAPI son los que serializa el record.
- Hoy el catálogo está vacío: el AsyncAPI documenta el canal y el sobre, y la plantilla de un evento queda comentada al final del archivo.

### Lint y cambios incompatibles

- Job `contratos` en `ci.yml`: instala **vacuum v0.32.0** y **oasdiff v1.33.0** desde sus releases de GitHub verificando el SHA-256 fijado en el workflow (como gitleaks) y corre `vacuum lint` con `contracts/vacuum/openapi.yaml` y `contracts/vacuum/asyncapi.yaml`. Falla con cualquier error.
- `contracts/vacuum/openapi.yaml`: `recommended` + todas las reglas OWASP; como error, los mínimos de ATLAS-ADR-0010 (operationId únicos y presentes, operaciones etiquetadas y descritas, seguridad declarada en toda operación que cambia estado, nada sensible en la URL, solo HTTPS, sin esquemas inseguros). Límites de tasa (429, `X-RateLimit-*`) y errores con cuerpo (500, 400/422) quedan como advertencia hasta la HU 1.3b y problem+json; los límites de tamaño de esquemas, como información.
- `contracts/vacuum/asyncapi.yaml`: `recommended` de AsyncAPI, sin `asyncapi-servers` ni `asyncapi-3-tags` (fallan en v0.32.0; se reemplaza la segunda por una regla propia sobre `info.tags`).
- En los PR, `oasdiff breaking` contra el `atlas.v1.yaml` de la rama base **solo advierte** (anotaciones `::warning`) y el `changelog` va al resumen del job. Bloquear con `--fail-on ERR` queda para cuando el contrato se publique (F3), como pide ATLAS-ADR-0010.

## Consecuencias

### Positivas

- El contrato no se puede desactualizar sin que el CI lo note, y el arranque de producción no tiene ramas para el generador.
- Ninguna superficie nueva en qa y pdn.
- El AsyncAPI y el código no se pueden separar: agregar un evento obliga a documentarlo en el mismo PR.

### Negativas

- Regenerar el OpenAPI exige correr una prueba (con Docker, por el fixture de Testcontainers del proyecto), no basta con compilar.
- El AsyncAPI se mantiene a mano; las pruebas cubren tipos, versiones y el sobre, no el esquema de `data` de cada evento.
- Se apagan dos reglas de vacuum por fallas de la herramienta; hay que revisarlas al subir de versión.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| 2. Generación en build | Ejecuta el `Program` en cada build: exige configuración falsa (o una rama en el arranque) y arranca servicios en segundo plano |
| 4. Generador de AsyncAPI | Las librerías .NET de AsyncAPI no son maduras para 3.0 ni conocen la outbox propia; un catálogo tipado con pruebas da la misma garantía con menos dependencias |

## Más información

- Notas para las demás HU: todo endpoint nuevo lleva `WithName`, `WithTags`, `WithSummary` y `WithDescription`, y el PR incluye el `atlas.v1.yaml` regenerado. Todo evento nuevo es un `TipoEvento` y entra al AsyncAPI en el mismo PR.
- [por verificar] Buzón del equipo para `info.contact.email` del AsyncAPI.
- [por verificar] Soporte de AsyncAPI 3.1 en `@asyncapi/react-component` (visor de la PWA).
