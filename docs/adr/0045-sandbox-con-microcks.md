---
id: ATLAS-ADR-0045
titulo: Sandbox nivel 1 con Microcks — spec podado importado con un nombre opaco por organización y paquete, proxy en atlas-api con llave por sandbox guardada como hash y Microcks nunca expuesto
estado: propuesto
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-05, A-07, §5.1, §7.3, §8.4, §12 y §15, plan 00 D-08, §8.4, §8.5, §8.8 y §13, HU 4.7, ATLAS-ADR-0008, ATLAS-ADR-0023, ATLAS-ADR-0038, ATLAS-ADR-0041, ATLAS-ADR-0043]
---

# ATLAS-ADR-0045 · Sandbox nivel 1 con Microcks

## Contexto y problema

La HU 4.7 pide el sandbox nivel 1 (plan 00 §8.8): un cliente activa el sandbox de un paquete compartido y recibe respuestas
mock de **sus** operaciones; las no compartidas responden 404. El nivel 2 (credenciales contra QA real, P-07) no entra.

Hechos verificados el 24/10/2026:

- `AccesoPortal.PuedeUsarSandboxAsync` ya decide quién puede (compartición con `puede_sandbox`, nivel técnico y
  `atlas.sandbox.usar` o el permiso extra `sandbox` de la invitación, ATLAS-ADR-0043), y `SpecsDelPortal.PodarAsync` da el
  spec podado de un contrato (ATLAS-ADR-0038).
- Microcks 1.15.0 (05/08/2026, Apache-2.0, CNCF) se probó a mano y en Testcontainers: `POST /api/artifact/upload?mainArtifact=true`
  (multipart `file`) importa un OpenAPI 3.1 y toma el nombre y la versión del servicio de `info.title` e `info.version`
  (responde `nombre:versión`); `GET /api/services/{nombre}:{versión}?messages=false` lo busca (404 si no está);
  `DELETE /api/services/{id}` lo borra; los mocks viven en `/rest/{nombre}/{versión}/{ruta}`, una ruta que no está en el
  servicio responde 404 y no hay autenticación en `/rest`. La imagen «uber» guarda en memoria (MongoDB embebido) y no
  trae Keycloak; la variante `-native` arranca en un par de segundos.
- Todo `/api/**` que cambia estado sin `Authorization: Bearer` exige la cabecera antifalsificación (ATLAS-ADR-0008); el
  único precedente de ruta sin sesión dentro de un módulo es el feed `.ics` (token en la URL, guardado como hash).
- YARP no está en el repo todavía (ATLAS-ADR-0008 lo dejó para el tráfico interno).

## Factores de decisión

- Fuga entre organizaciones = el riesgo más alto (§15): una organización nunca debe poder llamar el mock de otra.
- Solo atlas-api se expone (A-07); Microcks es una pieza interna más.
- Lo más simple que cumpla el plan §8.8.
- Que revocar o vencer la compartición lo apague sin intervención.
- Sin credenciales en el repo; la llave del cliente nunca se guarda en claro.
- Microcks «uber» pierde todo al reiniciarse (y en la 177 se reiniciará).

## Opciones consideradas

### Qué se importa

1. **El spec podado por (organización, paquete, revisión)**: lo no compartido no existe en el mock.
2. El spec completo y un filtro de rutas por paquete en el borde (nginx o YARP).

### Cómo se aísla entre organizaciones

1. Solo el nombre opaco del servicio en Microcks, expuesto directo.
2. **Proxy en atlas-api con una llave por sandbox**; Microcks sin ingress.
3. Proxy en atlas-api con la sesión del portal (cookie).

### Cómo se mantiene

1. **Importar al activar, revisar en la compartición y en un trabajo periódico** (reimportar, apagar, reponer, borrar).
2. Importar en cada publicación estable compartible (lo que proponía el plan 00 §8.8 con el evento `version_publicada`).

## Decisión

Opción 1, 2 y 1.

### Importar el spec podado con un nombre opaco

- Activar poda (`SpecsDelPortal.PodarAsync`, ambiente `sandbox`) cada contrato **OpenAPI** del paquete que la persona ve
  con nivel técnico y con operaciones; AsyncAPI y proto no entran al nivel 1 (Microcks necesitaría brokers). Si no queda
  ninguno, 409 `sin_contratos_mock`.
- El spec se manda en JSON con `info.title = atlas-<ruta>-<contrato>` e `info.version = r<revisión>-<huella>`. `ruta` son
  128 bits aleatorios en hexadecimal por (organización, paquete), fijos mientras el sandbox siga activo; la huella (SHA-256
  de la revisión, contratos, versiones y operaciones) cambia la versión cuando cambia lo importado. Microcks nunca ve el
  nombre del paquete ni de la organización.
- Si un contrato falla al importar, se borran los que alcanzaron a entrar en ese intento (502 `microcks_fallo`).

### Proxy en atlas-api con llave (por qué no basta el nombre opaco)

El nombre opaco solo es «no adivinable» mientras nadie lo vea: queda en logs, en historiales de shell, en capturas, en
la consola de Microcks y en el tráfico de cualquier proxy; no se puede revocar ni rotar sin reimportar, y exponer Microcks
publicaría también su API de administración (importar y borrar servicios, sin autenticación en la imagen «uber»). Por eso:

- Microcks no tiene ingress; solo atlas-api lo alcanza por la red del clúster.
- El cliente llama `<Contratos:Sandbox:UrlPublica>/api/v1/contratos/sandbox/<ruta>/<contrato>/<ruta de la operación>`
  con `Authorization: Bearer <llave>`. La llave (`atlsbx_` + 256 bits en base64url) se muestra **una sola vez** al
  activar; se guarda su SHA-256 y se compara en tiempo constante. Activar otra vez un sandbox activo **regenera** la llave:
  la anterior deja de servir en ese instante.
- En cada llamada el proxy exige: la ruta existe, la llave es la de esa ruta, el sandbox está activo y sin vencer, y su
  compartición sigue vigente, no revocada, con `puede_sandbox` y con el paquete publicado. Cualquier falla es el mismo 404
  (no se revela si la ruta existe); sin llave, 401. La cookie no cuenta (una persona con sesión de B en la URL de A
  recibe 401).
- El proxy arma la URL de Microcks solo con lo guardado (servicio y versión) y escapa la ruta de la operación segmento por
  segmento, rechazando `.` y `..`: no se puede saltar a otro servicio. Quita `Authorization`, `Cookie`, `X-Forwarded-*`,
  `Origin` y las cabeceras `X-Atlas-*`; no devuelve `Set-Cookie` ni las cabeceras CORS de Microcks; cuerpo hasta 1 MiB.
- Usar `Bearer` evita la cabecera antifalsificación como en los demás clientes no navegador (ATLAS-ADR-0008), sin tocar
  la identidad. La ruta es anónima para la autorización (está en `AutorizacionTests.Publicos`) y no va al contrato OpenAPI
  de Atlas (reenvía cualquier ruta del contrato del cliente).
- Se descartó la cookie: los que llaman son sistemas del cliente (curl, su backend, Postman), no el navegador.
- Se reenvía con `HttpClient` (con la resiliencia estándar de los ServiceDefaults), no con YARP: es una sola ruta y así no
  se suma un paquete sin versiones recientes (ATLAS-ADR-0008).

### Datos (`contratos_sandbox`, migración `Sandbox`)

| Columna | Para qué |
|---|---|
| `organizacion_id`, `paquete_id` | Única por par. Llave foránea a `plataforma_organizacion` a mano; RLS forzada `organizacion_visible` y predicado de EF Core |
| `comparticion_id` | La compartición que lo permite (la misma fila aunque se revoque y se vuelva a compartir) |
| `nivel` | `CHECK nivel = 1` |
| `ruta` | La parte opaca de la URL y del nombre en Microcks (única) |
| `revision`, `contratos` (`jsonb`), `huella` | Lo importado: `[{contratoId, versionId, segmento, servicio, version, operaciones}]` |
| `estado`, `apagado_en`, `motivo_apagado` | `activo` o `apagado` (`revocada`, `vencida`, `sin_sandbox`, `paquete_no_disponible`, `sin_contratos`) |
| `llave_hash`, `llave_creada_en` | SHA-256 de la llave (32 bytes) |
| `activado_por`, `activado_en`, `expira_en`, `importado_en`, `ultimo_uso_en` | `expira_en` = `vigente_hasta` de la compartición; el último uso se escribe a lo sumo una vez por hora |
| `por_borrar` (`jsonb`) | Servicios `nombre:versión` que faltan por borrar en Microcks |

### Mantenimiento

- Cambiar o revocar una compartición revisa en ese momento su sandbox: se apaga (y se borra de Microcks) o se reimporta
  si cambió la revisión fijada.
- `TrabajoSandbox` (cada `Contratos:Sandbox:Intervalo`, 5 min): apaga los vencidos, revocados, sin sandbox o con el paquete
  archivado; reimporta si cambió la revisión vigente o lo compartido (nunca agrega contratos que quien lo activó no veía);
  **vuelve a importar** lo que Microcks ya no tiene (la imagen «uber» guarda en memoria); y reintenta `por_borrar`. Cada
  paso es idempotente, así que varias réplicas pueden correrlo.
- No se importa en cada publicación: casi ninguna versión publicada termina en un sandbox, y la importación por
  activación ya sigue la revisión que ve cada organización.
- Auditoría en la misma transacción: `contratos.sandbox_activado`, `_llave_regenerada`, `_reimportado` y `_apagado` (con la
  organización, el paquete, la revisión y los contratos; nunca la llave ni su hash).

### Endpoints

| Método y ruta | Permiso | Qué |
|---|---|---|
| `POST /api/v1/portal/sandbox` (`{paqueteId}`) | `portal.ver` + `PuedeUsarSandboxAsync` | 201 activado o reactivado, 200 llave regenerada, con la llave una vez. 404, 403 (`solo_resumen`, `no_permitido_por_comparticion`, `sin_permiso`), 409, 502, 503 `sandbox_no_disponible` |
| `GET /api/v1/portal/sandbox` | `portal.ver` | `disponible`, y los sandboxes de la organización de los paquetes donde la persona puede usarlo, con la URL base de cada contrato que ve, instrucciones y un curl con `$LLAVE_SANDBOX` |
| `GET /api/v1/contratos/organizaciones/{id}/sandboxes` | `paquetes.gestionar` | Para la administración, sin llave ni hash |
| `* /api/v1/contratos/sandbox/{ruta}/{contrato}/{**resto}` | llave del sandbox | El proxy |

Los dos del portal tienen su caso en `AislamientoPortalTests.Casos`. La matriz rol × permiso no cambia.

### Configuración (`Contratos:Sandbox`)

`UrlMicrocks` (sin ella, no disponible: el botón sale deshabilitado con «El sandbox no está disponible» y la API responde
503), `UrlPublica` (vacía = `Atlas:UrlPublica`; en local, la PWA de Aspire `https://localhost:4032`; para qa el líder
aceptó como propuesta `https://sandbox.qa.atlas.flitsas.online`, solo de ejemplo, no está en el código), `UrlToken`,
`ClienteId` y `ClienteSecreto` (si un día se usa Microcks con Keycloak: token `client_credentials` de su cuenta de
servicio, por SealedSecret), `Intervalo`, `TrabajoHabilitado`, `CuerpoMaximoKiB` y `TiempoEspera`.

### Aspire y PWA

- AppHost: `quay.io/microcks/microcks-uber:1.15.0-native` fijada por digest, sin puerto fijo, con su chequeo de salud;
  atlas-api recibe `Contratos__Sandbox__UrlMicrocks` y `__UrlPublica`.
- PWA: en el paquete y en el contrato compartido, el panel Sandbox (activar, URL base por contrato, la llave una sola vez
  con «Copiar», instrucciones y curl); el botón «Sandbox» de las acciones lleva al panel. La llave no se guarda en la caché
  de consultas ni sin conexión. Móvil primero y axe en el E2E.

### Pruebas

- `SandboxMicrocksTests` (categoría `Microcks`) con Microcks real en Testcontainers: activar, operación compartida con el
  ejemplo del spec, las no compartidas en 404 (y ausentes del servicio importado), sin llave 401, llave ajena o de otra
  organización 404 en las dos direcciones, la sesión de otra organización 401, regenerar invalida la anterior, revocar lo
  apaga y lo borra de Microcks, una revisión nueva se reimporta y lo que Microcks pierde se repone. Corre en la suite normal
  (y en el job de .NET del CI: la imagen pesa unos 460 MB y arranca en 1–2 s); se salta en local con
  `dotnet test … -- --filter-not-trait "Categoria=Microcks"`.
- `SandboxSinMicrocksTests`: sin Microcks, `disponible = false` y 503; lo ajeno sigue en 404; sin sandbox en la
  compartición, 403.
- `AislamientoPortalTests`: los dos endpoints del portal con un Microcks en memoria.

## Consecuencias

### Positivas

- Lo no compartido no existe en el mock: no hay un filtro que mantener en el borde.
- Una organización no alcanza el sandbox de otra ni con la URL: hace falta la llave, que es revocable y rotable.
- Microcks no se expone; su API de administración queda dentro del clúster.
- Revocar, vencer o quitar el sandbox corta en la siguiente llamada aunque el trabajo no haya corrido.

### Negativas

- El tráfico del sandbox pasa por atlas-api (carga, aunque los mocks son livianos). El límite de tasa por organización
  llega con la HU 4.10.
- La llave va en `Authorization`: en el sandbox el cliente la usa en lugar de su token real (Microcks no valida el suyo).
- La importación es síncrona en la petición de activar (menos de 1 s por contrato en las pruebas); con paquetes grandes
  habría que pasarla a la cola (§5.1).
- Microcks «uber» guarda en memoria: tras un reinicio, el sandbox responde 404 hasta la siguiente vuelta del trabajo.
- Solo OpenAPI; la calidad del mock depende de los `examples` de cada spec (regla de vacuum de lo compartible, plan 00 §8.8).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Spec completo y filtro en el borde | Otra pieza que mantener por paquete y organización, y un error en el filtro expone lo interno |
| Solo el nombre opaco, Microcks expuesto | Expone la API de administración, no se revoca ni se rota, y la URL se filtra fácil |
| Sesión del portal (cookie) | Quienes llaman son sistemas del cliente, no el navegador; la sesión es corta y exige MFA |
| Importar en cada publicación | Importa versiones que nadie usa y no sigue la revisión fijada de cada organización |
| YARP | Una sola ruta; `HttpClient` alcanza y evita sumar un paquete sin versiones recientes |

## Más información

Lo que el líder debe desplegar en la 177 (manifiestos en `flit-gitops`, no en este repo):

1. Deployment `atlas-microcks` en `atlas-qa` (y en `atlas-dev` si se usa) con `quay.io/microcks/microcks-uber:1.15.0-native`
   fijada por digest (`sha256:b36c02f9e52bd6dfad939bbbd75dfd3f27c3196a046caa34fabd82e76924f670`), puerto 8080, sondas a
   `/api/health` con tiempos holgados, límites de CPU y memoria (proponer 250m/512Mi) y una sola réplica.
2. Service ClusterIP **sin Ingress** y una NetworkPolicy que solo deje entrar a los pods de atlas-api.
3. En el ConfigMap de atlas-api: `Contratos__Sandbox__UrlMicrocks=http://atlas-microcks:8080` y
   `Contratos__Sandbox__UrlPublica` con el host que elija (propuesta: `https://sandbox.qa.atlas.flitsas.online`).
4. Si se usa ese host: registro DNS, certificado e Ingress hacia atlas-api **solo** con el prefijo
   `/api/v1/contratos/sandbox/`.
5. Si en pdn se usa Microcks con Keycloak: `Contratos__Sandbox__UrlToken`, `__ClienteId` y `__ClienteSecreto` en un
   SealedSecret.

Pendientes **[por verificar con el líder]**: el host público del sandbox por ambiente; si el sandbox va a pdn (el plan
lo deja solo en la 177 al inicio, §15); si regenerar la llave debe pedir reautenticación (§6.3; hoy no la pide); si un
contrato nuevo en una revisión debe entrar solo al sandbox o exigir que alguien lo vuelva a activar (hoy, lo segundo);
el límite de tasa del proxy (HU 4.10); y si conviene la imagen no «uber» de Microcks (con MongoDB persistente) cuando
haya clientes.
