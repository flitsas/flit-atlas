# F0.4 · Checklist de prerrequisitos

> **Fecha de corte:** 21/10/2026 · **Fuentes:** prompt maestro §6, §14 y §15; plan 00 §13; manual de operación de `flit-gitops`; ATLAS-ADR-0004, 0007, 0008, 0014 y 0015.
> **Estados:** ✅ verificado · 🔶 verificado con hallazgo (requiere acción) · ⏳ por verificar · 📋 pendiente de crear.
> **Responsables:** según el reparto v5 (`docs/atlas/insumos/Responsables_Productos_FLIT_2026-10-20.md`): Infraestructura → Daniel Mejía (principal) y Jorman Copete (secundario); Identidad → Samuel Cárdenas (principal) y Jorman Copete (secundario); Atlas y Plataforma → Jorman Copete. Lo que se hace en la consola de Microsoft 365 lo **gestiona** el responsable indicado y lo **ejecuta** quien tenga el rol de administrador global de FLIT **[por confirmar quién es]**.

Lo que bloquea una HU concreta de F1 se marca en la columna «Bloquea».

## 1. Repositorio y CI

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 1.1 | `flitsas/flit-atlas` privado, rama predeterminada `develop` | ✅ | `gh repo view`: `PRIVATE`, `develop` | Jorman Copete | — |
| 1.2 | Protección de `develop`, `staging` y `release` | ✅ | `gh api …/protection`: 1 aprobación, CODEOWNERS obligatorio, checks `markdownlint` y `gitleaks` en las tres | Jorman Copete | — |
| 1.3 | CI mínimo en verde | ✅ | Ejecuciones del 21/10/2026 en las tres ramas: `success` | Jorman Copete | — |
| 1.4 | Runners propios de la 177 para `flit-atlas` (repo privado, consume minutos) | ⏳ | Registrar el repo en el grupo de runners de la organización o aceptar `ubuntu-latest` | Daniel Mejía | HU 1.1 (CI de cuatro lenguajes) |
| 1.5 | Visibilidad de `flitsas/flit-gitops` | ✅ | `PRIVATE`. Cierra la D-5 del plan 01 | Jorman Copete | — |
| 1.6 | Visibilidad de `flitsas/flit` | 🔶 | Sigue `PUBLIC` (P-01). Afecta F4, no F1 | Jorman Copete | F4 |

## 2. Base de datos

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 2.1 | `pgvector` en el Postgres 16 de la 177 (dev y qa) | ⏳ | La base es del host, no del clúster: no hay rastro en `flit-gitops`. Verificar con `SELECT * FROM pg_available_extensions WHERE name='vector'`; si no está, instalar el paquete del sistema | Daniel Mejía | HU 1.1 en el clúster (en local lo da la imagen de Aspire) |
| 2.2 | `pgvector` en el Postgres 16 de la 169 (pdn) | ⏳ | Igual que 2.1. Instalarlo implica un reinicio de Postgres: programarlo un domingo, como los del 11 y el 12/10/2026 | Daniel Mejía | F4 (salida a pdn) |
| 2.3 | `unaccent` en 177 y 169 | ⏳ | Es de `contrib`, normalmente disponible. Verificar con la misma consulta | Daniel Mejía | HU 1.2 |
| 2.4 | Esquema `atlas` y usuario `flit_atlas` por ambiente | 📋 | `servicio-con-esquema-propio.sql -v servicio=atlas` y `CREATE EXTENSION vector, unaccent` como administrador. Línea en `pg_hba.conf` para la red de pods, como se hizo con `flit2-dev` | Daniel Mejía | HU 1.8 |
| 2.5 | En qué base vive el esquema `atlas` de cada ambiente | ⏳ | La propuesta es la misma base de FLIT 2.0 del ambiente. En pdn el respaldo diario cubre `flitpdn` y `flit`; confirmar que el esquema quede dentro de una de ellas | Jorman Copete | HU 1.8 |
| 2.6 | RLS con PgBouncer en modo session (pdn) | ⏳ | ATLAS-ADR-0007: el contexto de organización se fija con `SET` por conexión. Probar en qa antes de F4 | Samuel Cárdenas | HU 1.4 en pdn |

## 3. Capacidad de las VPS

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 3.1 | CPU y RAM libres en la 177 para `atlas-dev`, `atlas-qa` y `atlas-sandbox` | ⏳ | Medir peticiones contra capacidad asignable del nodo. Presupuesto inicial de Atlas por ambiente **[por estimar en HU 1.8]** | Daniel Mejía | HU 1.8 |
| 3.2 | CPU en la 169 para `atlas-pdn` | 🔶 | Manual de operación (12/10/2026): presión de CPU del 30 %, 7,3 de 10,5 CPU asignables ya pedidas, CPU de un hilo 2,2 veces más lenta que la 177. Atlas en pdn necesita cuota propia y una medición nueva antes de F4 | Daniel Mejía | F4 |
| 3.3 | Latencia desde Colombia a la 169 (~190 ms) | 🔶 | Afecta el LCP < 2,5 s en 4G de la PWA (§11). Mitigación en el diseño: shell precargado y caché offline. La decisión estructural (Cloudflare o VPS en América) sigue abierta en `flit-gitops` | Jorman Copete | F4 (Lighthouse en pdn) |
| 3.4 | Sondas con tiempos holgados | 📋 | Copiar el patrón del 16/10/2026: liveness `timeoutSeconds: 5`, `failureThreshold: 6` y `startupProbe` de 5 min | Jorman Copete | HU 1.8 |

## 4. Almacenamiento, colas y caché

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 4.1 | Bucket o prefijo `atlas/` en MinIO de dev y qa | ⏳ | En la 177 cada stack tiene su MinIO propio con hostPath. Decidir: MinIO propio en `atlas-*` (recomendado, aislado) o prefijo en uno existente | Daniel Mejía | HU 1.8 |
| 4.2 | Bucket `atlas` en Contabo (pdn) | ⏳ | Separado de adjuntos y de `flit-db-backups`. Firma en cabecera o query (Contabo rechaza el POST policy) | Daniel Mejía | F4 |
| 4.3 | RabbitMQ por ambiente con el exchange `flit.atlas` | 🔶 | dev tiene RabbitMQ en `flit2-dev`; **qa no tiene** (manual, 18/10/2026) y `services/flit2-pdn` tampoco lo declara. Decidir si Atlas usa el de FLIT 2.0 (con vhost propio) o uno propio | Daniel Mejía | HU 1.8 |
| 4.4 | Redis por ambiente | 🔶 | Ningún `flit2-*` declara Redis; solo los satélites (p. ej. `flito-dev`) tienen el suyo. Propuesta: Redis propio en `atlas-*` (caché, Data Protection, rate limiting) | Daniel Mejía | HU 1.8 |
| 4.5 | `Flit.Platform.Sdk` publicado en GitHub Packages | 🔶 | No hay workflow de publicación en `flit` (ATLAS-ADR-0015). PR en `flit` o plan B de ATLAS-ADR-0014 | Jorman Copete | HU 1.2 (outbox) |

## 5. Microsoft Entra ID y Microsoft Graph

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 5.1 | Aplicación «Flit Atlas» registrada **por ambiente** (dev, qa, pdn), tenant único | 📋 | Tres registros, para que un secreto de dev nunca sirva en pdn. Redirect URIs: `https://<host>/signin-oidc` y `https://<host>/signout-callback-oidc`. Para local, `http://localhost:4031/signin-oidc` solo en el registro de dev | Samuel Cárdenas | HU 1.3 |
| 5.2 | Credencial de cliente: certificado (no secreto) en SealedSecret | 📋 | Un certificado por ambiente, con fecha de vencimiento anotada y recordatorio | Samuel Cárdenas | HU 1.3 |
| 5.3 | Permisos delegados para el login | 📋 | `openid`, `profile`, `email`, `offline_access`, `User.Read` | Samuel Cárdenas | HU 1.3 |
| 5.4 | Permisos de aplicación para la sincronización diaria | 📋 | `User.Read.All` y `GroupMember.Read.All` (foto, cargo, área, estado y grupos). Consentimiento de administrador documentado | Samuel Cárdenas | HU 1.3 (JIT y mapeo de grupos) |
| 5.5 | `Calendars.ReadWrite` restringido al calendario «Soporte TI sábados» | 📋 | Permiso de aplicación limitado a ese buzón con RBAC para aplicaciones de Exchange Online **[verificar: las políticas de acceso de aplicaciones de Exchange están en retiro a favor de RBAC para aplicaciones]** | Samuel Cárdenas | F2 |
| 5.6 | `Mail.Send` restringido al buzón «Invitaciones Atlas» | 📋 | Misma restricción que 5.5. Alternativa: SMTP autenticado (ATLAS-ADR-0004) | Samuel Cárdenas | HU 1.5 |
| 5.7 | Aviso en un canal de Teams | ⏳ | Los conectores de webhook entrante de Office 365 se retiraron. Opciones: flujo de Workflows (Power Automate) con URL propia, o un bot. Graph no permite publicar en canales con permiso de aplicación salvo en migraciones **[verificar mecanismo vigente]** | Samuel Cárdenas | F2 |
| 5.8 | El token de Entra trae `amr` con `mfa` para todos los empleados | ⏳ | ATLAS-ADR-0004. Revisar la política de acceso condicional; si no siempre lo emite, Atlas no puede exigirlo | Samuel Cárdenas | HU 1.3 |
| 5.9 | Grupos de Entra para el mapeo a roles | ⏳ | Listar los grupos que existen (p. ej. `FLIT-Developers`) y proponer el mapeo a `atlas.*` | Jorman Copete | HU 1.4 |
| 5.10 | Buzón compartido «Soporte TI sábados» con su calendario | 📋 | Crear en Microsoft 365 | Jorman Copete | F2 |
| 5.11 | Buzón compartido «Invitaciones Atlas» | 📋 | Crear en Microsoft 365. Dirección **[por definir]** | Jorman Copete | HU 1.5 |
| 5.12 | Entra multi-tenant para externos con Microsoft 365 (opcional, detrás de bandera) | ⏳ | Evaluar en F0.5 (pregunta N-07). No bloquea | Samuel Cárdenas | — |

## 6. Identidad propia (OpenIddict y sesión)

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 6.1 | Certificados de **firma** de OpenIddict por ambiente | 📋 | X.509 en SealedSecret; JWKS publicado. Nunca el certificado de desarrollo fuera de local | Samuel Cárdenas | HU 1.3c |
| 6.2 | Certificados de **cifrado** de OpenIddict por ambiente | 📋 | Igual que 6.1 | Samuel Cárdenas | HU 1.3c |
| 6.3 | Plan de rotación de los certificados 6.1 y 6.2 | 📋 | Doble llave durante la transición (la vieja solo valida). Documentarlo en el manual de operación de `flit-gitops` | Samuel Cárdenas | HU 1.3c |
| 6.4 | Llaves de Data Protection compartidas entre réplicas | 📋 | En Redis (4.4) o en la base, protegidas con un certificado en SealedSecret | Samuel Cárdenas | HU 1.3 |

## 7. GitHub

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 7.1 | GitHub App de solo lectura para CODEOWNERS y repos | 📋 | Permisos `contents: read` y `metadata: read`, instalada solo en los repos del catálogo. Recomendado: una app propia de Atlas, no la que ya usa Argo CD para leer `flit-gitops` (mínimo privilegio y rotación independiente) | Jorman Copete | F2 |
| 7.2 | Acceso del `GITHUB_TOKEN` de `flit-atlas` al paquete NuGet del SDK | 📋 | Configuración de acceso del paquete, después de 4.5 | Jorman Copete | HU 1.2 |
| 7.3 | OIDC de GitHub Actions para publicar en Atlas | ✅ | No requiere nada previo: la lista blanca vive en Atlas (ATLAS-ADR-0009). Se configura en F3 | Jorman Copete | — |

## 8. DNS y borde

| # | Ítem | Estado | Evidencia o acción | Responsable | Bloquea |
|---|---|---|---|---|---|
| 8.1 | `dev.atlas.flitsas.online` y `qa.atlas.flitsas.online` → 177 | 📋 | Registro DNS, certificado con certbot en el nginx del host y sitio hacia la ClusterIP del Ingress de Atlas | Daniel Mejía | HU 1.8 |
| 8.2 | `atlas.flitsas.com` → 169 | 📋 | Agregar al mapa SNI del borde de la 169, como los dominios de FLIT 2.0 (11/10/2026). Depende de confirmar el dominio (N-01) | Daniel Mejía | F4 |
| 8.3 | Reglas de borde equivalentes a las de FLIT 2.0 (WAF, ModSecurity, límites) | ⏳ | ATLAS-ADR-0008: Atlas no pasa por `Flit.Gateway`. Listar qué aplica hoy el borde y replicarlo en el Ingress de Atlas | Daniel Mejía | HU 1.8 |

## 9. Fuera de F1 (se verifican al llegar a su fase)

| # | Ítem | Fase | Responsable |
|---|---|---|---|
| 9.1 | Microcks en `atlas-sandbox` (177) | F4 | Daniel Mejía |
| 9.2 | Gateway LiteLLM en `ia-gateway` (≥ 1.83.0, por digest) | F-IA | Jorman Copete |
| 9.3 | `python-ml` sirviendo bge-m3 con capacidad para la ingesta | F7 | Jorman Copete |
| 9.4 | URL vigentes de las APIs ICT por ambiente (lista de inconsistencias del Excel) | F3 | Juan Montoya (principal de ICT) |

## 10. Resumen de bloqueos para arrancar F1

- **HU 1.1 y 1.2** se pueden empezar ya en local. Solo el outbox de HU 1.2 depende de 4.5 (o del plan B).
- **HU 1.3** necesita 5.1 a 5.4 y 5.8 para la prueba con una cuenta real. Mientras tanto se avanza con el proveedor simulado de dev.
- **HU 1.8** necesita 2.1, 2.4, 3.1, 4.1, 4.3, 4.4, 8.1 y 8.3.
