# F0.3 · Propuesta de puertos y namespaces

> **Estado:** propuesta, pendiente de confirmar con el líder técnico. **No** se registra en `docs/despliegue-y-puertos.md` de `flit` ni en `flit-gitops` hasta que se confirme (prompt maestro §5.3).
> **Fecha:** 21/10/2026 · **Fuentes:** prompt maestro §5 y §14; plan 00 §4 (D-13) y §13; ATLAS-ADR-0006, 0008 y 0013.

## 1. Qué se verificó

Verificado el 21/10/2026 en modo lectura:

| Fuente | Hallazgo |
|---|---|
| `flit/docs/despliegue-y-puertos.md` §2.c y §2.d | Esquema `40xx` (local y dev), `50xx` (qa), `60xx` (pdn). Ocupados: `x001`, `x002`, `x003`, `x012`, `x020`–`x025`, `x028`, `x030`. El `x031` en adelante está libre en el registro. gRPC internos `8082`, `8083`, `8084` y `8086`, con el mismo número en todos los ambientes |
| Servicios de `flit-gitops/services/flit2-dev` | Además usan `4026` (core-consultas), **`4027` y `8085` (core-notificaciones), que no están en el registro de `flit`**. Conviene registrarlos allá para que nadie los reutilice |
| Servicios de `flit-gitops/services/flito-dev` | `4051` y `4052` (web y API de FLITO) |
| Búsqueda de `403x` y `8087`–`8089` en `flit-gitops` | Sin uso, salvo `4030` (migración) |
| Namespaces en `flit-gitops` | Cada producto tiene namespace propio por ambiente: `flit2-{dev,qa,pdn}`, `flito-{dev,qa}`, `flit-firmas-{qa,pdn}`, `filemanager-{dev,qa}`. El legado vive en `flit-apps` |
| Borde | El nginx del host termina TLS y reenvía a una ClusterIP fija por servicio (manual de operación de `flit-gitops`, 17 y 18/10/2026). No se publican puertos en el host |

Las ClusterIP fijas son un dato de infraestructura interna: se asignan en `flit-gitops` siguiendo su convención y **no** se escriben en este repo.

## 2. Puertos propuestos

Se mantienen los que ya reservó el plan 00 (D-13) para la API y la web, y se agrega el bloque contiguo `x033`–`x035`.

| Servicio | Local y dev | qa | pdn | ¿Expuesto? | Variable |
|---|---|---|---|---|---|
| `atlas-api` (REST, BFF, SignalR, OIDC de OpenIddict) | `4031` | `5031` | `6031` | **Sí**, único destino del borde | `ATLAS_API_PORT` |
| `atlas-web` (PWA estática) | `4032` | `5032` | `6032` | Sí, mismo host que la API (ver §3) | `ATLAS_WEB_PORT` |
| `atlas-render` (Go, REST interno: health y métricas) | `4033` | `5033` | `6033` | No | `ATLAS_RENDER_PORT` |
| `atlas-ia` (Python, REST interno: health, SSE del asistente) | `4034` | `5034` | `6034` | No | `ATLAS_IA_PORT` |
| `atlas-docs` (Python, REST interno: health y métricas) | `4035` | `5035` | `6035` | No | `ATLAS_DOCS_PORT` |

gRPC interno (solo red del clúster, h2c, mismo número en todos los ambientes, como en `flit` §2.d):

| Servicio | Puerto | Variable |
|---|---|---|
| `atlas-render` | `8087` | `ATLAS_RENDER_GRPC_PORT` |
| `atlas-ia` | `8088` | `ATLAS_IA_GRPC_PORT` |
| `atlas-docs` | `8089` | `ATLAS_DOCS_GRPC_PORT` |

`atlas-api` no expone gRPC en el MVP: solo es cliente. Si más adelante lo necesita (por ejemplo, para que `atlas-ia` pida datos con el contexto de permisos firmado), se usa `8090`, previa confirmación.

Dependencias en local (.NET Aspire): Postgres, Redis, RabbitMQ y MinIO toman puertos **dinámicos** que asigna Aspire, para no chocar con el Postgres `5432` que cada desarrollador ya tiene para FLIT 2.0. Los servicios de Atlas usan los fijos de la tabla, así las URL de los README y las redirect URIs de Entra ID en local no cambian.

## 3. Mismo origen para la PWA y la API

La cookie del BFF es `SameSite=Strict` (ATLAS-ADR-0008), así que la PWA y la API deben compartir host. Propuesta para cada ambiente:

| Ruta en el host de Atlas | Destino |
|---|---|
| `/api/`, `/hubs/`, `/connect/`, `/.well-known/`, `/signin-oidc`, `/signout-callback-oidc`, `/health/` | `atlas-api` |
| todo lo demás | `atlas-web` |

`atlas-web` es una imagen de nginx sin privilegios que sirve el build de Vite con las cabeceras de caché del service worker. Se descartó que `atlas-api` sirva los estáticos: acopla los despliegues del frontend y del backend, y obliga a reiniciar la API para publicar un cambio de interfaz. El enrutamiento se hace en el Ingress de Atlas, no en `Flit.Gateway` (ATLAS-ADR-0008).

## 4. Namespaces propuestos

| Ambiente | Clúster | Namespace | Application de Argo CD | Raíz que la lee |
|---|---|---|---|---|
| dev | k3s 177 | `atlas-dev` | `atlas-dev` | `clusters/vps-177/` |
| qa | k3s 177 | `atlas-qa` | `atlas-qa` | `clusters/vps-177/` |
| pdn | k3s 169 | `atlas-pdn` | `atlas-pdn` | `clusters/vps-169/` |
| sandbox (Microcks, F4) | k3s 177 | `atlas-sandbox` | `atlas-sandbox` | `clusters/vps-177/` |

Recomendación: **namespaces propios**, no los `flit2-*`.

- Es el patrón vigente en `flit-gitops`: cada producto tiene el suyo (FLITO, Firmas, file-manager).
- Cuota de recursos, `LimitRange` y `NetworkPolicy` propias. Importa en la 169, que ya va justa de CPU (manual de operación, 12/10/2026): una cuota evita que un exporte o una indexación de Atlas le quite CPU a FLIT 2.0 en pdn.
- La `NetworkPolicy` deja entrar a `atlas-render`, `atlas-ia` y `atlas-docs` solo desde `atlas-api` y desde el mismo namespace, y permite la salida de `atlas-ia` hacia `ia-gateway` (LiteLLM) y `python-ml`.
- Un SealedSecret de `atlas-dev` no se puede leer desde `flit2-dev`, y al revés.
- El costo es que Atlas debe pedir acceso explícito a lo que vive en `flit2-*` (RabbitMQ de dev, `python-ml`, `core-notificaciones` si se usa). Se resuelve con reglas de salida en la `NetworkPolicy` y DNS de servicio entre namespaces.

Secretos: en la 177, sellados con su llave propia. En la 169, con la llave compartida (regla del manual de operación del 10/09/2026) solo si el secreto debe abrirse también en el respaldo. Si no, con la llave de la 169.

## 5. Hosts

Sin cambios frente a ATLAS-ADR-0013: `dev.atlas.flitsas.online`, `qa.atlas.flitsas.online` y `atlas.flitsas.com` **[por confirmar el dominio .com]**.

## 6. Qué se hace al confirmar

1. PR en `flit`: agregar las filas de Atlas (y las de `core-notificaciones`, `4027`/`8085`) a `docs/despliegue-y-puertos.md` §2.d.
2. PR en `flit-gitops` (HU 1.8): namespaces, cuotas, `NetworkPolicy`, Applications y ClusterIP fijas.
3. Actualizar ATLAS-ADR-0006 (namespaces) y este documento a estado «confirmado».
