---
id: ATLAS-ADR-0051
titulo: Almacenamiento de objetos — Atlas usa el S3 de FLIT 2 de cada ambiente (MinIO del file-manager en qa, Contabo en pdn) con bucket y usuarios propios, y firma las descargas contra su propio dominio
estado: aceptado (decisión del líder, 26/10/2026; los puntos de pdn siguen [por verificar])
fecha: 2026-10-26
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-05 y A-06, ATLAS-ADR-0019, ATLAS-ADR-0042, ATLAS-ADR-0048, flit-gitops services/filemanager-qa y services/file-manager-minio, flit ADR-0057]
---

# ATLAS-ADR-0051 · Almacenamiento de objetos: el S3 de FLIT 2 de cada ambiente

## Contexto y problema

Las exportaciones de la F4 (ATLAS-ADR-0042) necesitan un S3: atlas-api sube la entrada y borra las vencidas, atlas-docs
deja los archivos y el navegador los descarga con una URL firmada de 15 minutos. QA no tenía un S3 para Atlas, y el
borrador del PR #76 de flit-gitops traía un MinIO propio en `atlas-qa`, con su host público, su certificado y su ClusterIP
fija. El ADR 0042 dejaba [por verificar] el host público del S3 de cada ambiente.

FLIT 2 ya resuelve lo mismo por ambiente:

- **dev y qa (177):** el file-manager habla con un MinIO interno de su namespace (`filemanager-dev`, `filemanager-qa`;
  Service `minio`, ClusterIP fija `10.43.51.9` en qa) y firma las URL contra el dominio de la aplicación
  (`qa.flitsas.online`). El nginx del host reenvía `/flit-files/` al MinIO **conservando el Host**, que es lo que pide la
  firma SigV4.
- **pdn:** `file-manager-minio` usa **Contabo Object Storage** (`eu2.contabostorage.com`, bucket creado en el panel) y
  firma contra `flitsas.online`; un `location` reenvía a Contabo conservando el Host. Así evita el firewall de un cliente
  que bloquea Contabo y el CORS, que el bucket no tiene.

El 26/10/2026 el líder decidió que Atlas use ese mismo almacenamiento.

## Factores de decisión

- No sumar otro MinIO, otro host público ni otro certificado para una función pequeña (exportaciones con 7 días de
  retención).
- Aislar los datos de Atlas de los de FLIT 2 aunque compartan el servidor.
- Que la descarga funcione detrás de firewalls de clientes y sin CORS, como en FLIT 2.
- Que nada cambie en el código de atlas-api ni de atlas-docs: los dos ya hablan S3 por rutas, con sumas de verificación
  solo cuando se requieren (ATLAS-ADR-0042).

## Opciones consideradas

1. MinIO propio en `atlas-qa` con host público aparte (el borrador del PR #76).
2. RustFS propio en `atlas-qa`.
3. El S3 de FLIT 2 de cada ambiente, con bucket y usuarios propios, firmando contra el dominio de Atlas.

## Decisión

Opción elegida: «el S3 de FLIT 2 de cada ambiente», por decisión del líder y porque reutiliza un patrón que ya funciona
en los tres ambientes.

| | qa (177) | pdn (169) |
|---|---|---|
| Servidor | MinIO del file-manager de QA (`filemanager-qa`) | Contabo Object Storage (`eu2.contabostorage.com`) |
| Endpoint interno (`Plataforma:Almacenamiento:Endpoint`, `ATLAS_DOCS_S3_ENDPOINT`) | `http://minio.filemanager-qa.svc.cluster.local:9000` | `https://eu2.contabostorage.com` |
| Bucket | `atlas-qa` | `atlas-pdn-eu2` (propuesto; se crea en el panel) |
| Credenciales | Usuarios `atlas-api` y `atlas-docs` del MinIO, con una política que solo alcanza `atlas-qa` | Las de Contabo [por verificar, abajo] |
| Host que firma (`EndpointPublico`) | `https://qa.atlas.flitsas.com` | `https://atlas.flitsas.com` |
| Reenvío | nginx del host: `/atlas-qa/` → `10.43.51.9:9000`, conservando el Host, solo `GET` y `HEAD` | `location /atlas-pdn-eu2/` → Contabo, conservando el Host [por verificar el mecanismo en la 169] |
| Ciclo de vida | Regla de 7 días en `exportaciones/` | [por verificar] que Contabo la soporte; si no, solo `VencedorExportaciones` |

Reglas:

- **Bucket propio con prefijo `atlas-`.** La URL firmada queda en el mismo origen que la PWA
  (`https://<host de Atlas>/<bucket>/exportaciones/...`). El service worker excluye `/^\/atlas-[a-z0-9-]+\//` de la
  navegación de la SPA; si no, contestaría la descarga con `index.html`. **Ninguna ruta de la PWA puede empezar por
  `/atlas-`.**
- **Mismo origen.** El S3 no necesita CORS, la CSP de la PWA queda con `connect-src 'self'` (`ATLAS_S3_PUBLICO` lleva el
  mismo origen, sin efecto) y compartir desde el móvil (HU 4.6) baja el archivo con `fetch` sin excepción.
- **Atlas no administra el servidor.** El bucket, la política y los usuarios de QA los crea una vez el script
  `gen-atlas-qa-s3.sh` de flit-gitops con la raíz del MinIO del file-manager, sin guardarla. atlas-api y atlas-docs nunca
  reciben la raíz; `CrearBucket` y `ATLAS_DOCS_S3_CREAR_BUCKET` siguen apagados fuera de local.
- **Red:** la NetworkPolicy de `filemanager-qa` deja entrar al MinIO solo a `atlas-api` y `atlas-docs` del namespace
  `atlas-qa`, en el puerto 9000.
- No hay Atlas en dev (ATLAS-ADR-0019).

Este ADR completa el [por verificar] del host público del S3 en ATLAS-ADR-0042 y reemplaza el MinIO propio del borrador
del PR #76.

## Consecuencias

### Positivas

- Sin MinIO, host público, certificado ni ClusterIP nuevos en QA; menos piezas que operar.
- Descargas en el mismo origen: sin CORS, sin excepciones en la CSP y sin el firewall de clientes que bloquea Contabo.
- Los datos de Atlas quedan en su bucket y sus usuarios no alcanzan los de FLIT 2.

### Negativas

- Atlas comparte el dominio de falla del MinIO del file-manager de QA: si se cae, no hay exportaciones (atlas-api responde
  503 `almacenamiento_no_disponible` y atlas-docs no pasa ready); y una exportación grande ocupa disco de ese MinIO.
- Hay que tocar la NetworkPolicy de `filemanager-qa`, que es de FLIT 2.
- Cambiar el nombre del bucket obliga a mover también el `location` del nginx y a respetar el prefijo `atlas-`.
- [por verificar, pdn] Las credenciales de Contabo son **de toda la cuenta**, no por bucket (README de
  `services/file-manager-minio`). Con ellas atlas-api y atlas-docs alcanzarían también los adjuntos de FLIT 2 en pdn. Antes
  de la HU 4.11 hay que elegir: aceptar ese riesgo, usar una cuenta o un almacenamiento de Contabo aparte para Atlas, o
  comprobar si Contabo ya permite llaves por bucket.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| MinIO propio en `atlas-qa` | Otro servidor, host, certificado y ClusterIP para lo mismo que ya da el file-manager; el líder prefirió reutilizar |
| RustFS propio | Igual que el anterior, y además una pieza que FLIT no opera todavía |
| Firmar contra un host propio del S3 | Necesita DNS, certificado y CORS; el mismo origen los evita, como hace FLIT 2 |
| Reescribir la ruta en el nginx (p. ej. `/almacen/` → bucket) | La firma SigV4 incluye la ruta: reescribirla la invalida |

## Más información

- flit-gitops: `services/filemanager-qa/` (MinIO de QA y su NetworkPolicy), `services/file-manager-minio/README.md`
  (Contabo, PUT presignado, firma contra el dominio de la aplicación), `services/atlas-qa/README.md` (activación).
- Atlas sube con `PutObject` desde el servidor, no con el POST presignado que Contabo rechaza; el navegador solo baja con
  `GET` firmado.
- [por verificar, pdn] Región para SigV4 con Contabo (FLIT 2 la deja vacía y la descubre; Atlas la pide explícita en
  `Plataforma:Almacenamiento:Region`).
