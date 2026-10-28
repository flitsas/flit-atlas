# Estado del trabajo · Flit Atlas

> **Corte:** 26/10/2026, noche (hora de Bogotá). Archivo vivo para retomar el trabajo en una sesión nueva.
> Se actualiza al cerrar cada jornada. El prompt maestro (`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`) sigue
> siendo la fuente que prevalece; aquí solo va **dónde quedamos** y **qué sigue**.

## 1. Dónde estamos

| Fase | Estado | Dónde |
|---|---|---|
| F0–F2 | Integradas en `develop` y desplegadas en QA | — |
| F3 · Contratos internos y legado ICT | Integrada en `develop` (PR #29) y en `staging` (PR #30, 24/10). El CD desplegó en QA (gitops `07e5eb1`, «atlas-qa: despliegue cf56f28») | Tag `v1` del workflow reutilizable sobre `b957751` |
| **F4 · Portal, exportes y pdn** | **Código completo hasta la ola 3 + correcciones de seguridad.** Falta la ola 4 (despliegue) | **PR #31 integrado en `develop`** el 26/10 (`f635cce`, cabeza `c8c64ee`). Aún no va a `staging`: ver sección 3 |

### Lo que ya trae la rama `f4/portal-integracion`

| HU | Qué | ADR |
|---|---|---|
| 4.1 | Paquetes con revisiones inmutables, asistente de 3 pasos, publicar y fijar revisión anterior | 0041 |
| 4.2 | Poda de specs OpenAPI/AsyncAPI con lista blanca de extensiones, fail-closed | 0038 |
| 4.3 / 4.4 | Comparticiones con organizaciones (RLS), portal del cliente y pruebas de aislamiento bloqueantes | 0043 |
| 4.5 | Exportes en atlas-docs (7 formatos, marca de agua, hash) por cola RabbitMQ, URL firmada de 15 min, token de servicio OpenIddict | 0042, 0044 |
| 4.6 | Compartir desde el móvil (Web Share, descargas en iOS) | 0042 §3 |
| 4.7 | Sandbox nivel 1 con Microcks detrás de un proxy en atlas-api con llave por compartición | 0045 |
| 4.8 / 4.9 | Avisos de deprecación (bandeja, push, correo) y favoritos sin conexión | 0046, 0034 |
| 4.10 | CSP estricta, cabeceras, límites de tasa por organización, prueba de carga k6, runbook (`deploy/runbook-atlas.md`), revisión de seguridad y pentest de identidad (`docs/seguridad/revision-f4.md`) | 0048 |
| Seguridad | Correcciones de identidad (F4-SEC-01 a 08, 13 y bajos) y de datos (F4-SEC-09 a 12 y bajos) | 0049, 0050 (**aceptados por el líder el 24/10**) |
| F4-SEC-02 (26/10) | Un fallo rezagado ya no suma con la cuenta bloqueada (contraseña y segundo factor): evitaba un segundo bloqueo que duplicaba la duración. Queda como `inicio_rechazado` | 0049 |

Última verificación local: `dotnet test Flit.Atlas.slnx` **788 de 789** (la omitida es la prueba de carga, que corre solo con
`ATLAS_CARGA_K6`). Web: Vitest 218/218 en serie, bundle inicial ~176,5 KB gzip. atlas-docs: pytest en verde (el PDF corre en
el contenedor con Pango y `ATLAS_DOCS_EXIGIR_PDF=1`). El CI del PR #31 sobre `056d541` falló en una prueba de 798
(`Pentest_veinte_contrasenas_erradas_concurrentes_bloquean_la_cuenta`, carrera real de F4-SEC-02, intermitente); se corrigió
en `c8c64ee` y el PR se integró. Revisar el CI de `develop` sobre `f635cce`.

## 2. Ola 4: lo que ya quedó listo

1. **Limpieza de descripciones del contrato ICT**: integrada en `f4/portal-integracion` (`8078bad`). El generador
   `atlas_docs.ict` falla si un texto de cara al cliente nombra piezas internas (`ict/lenguaje.py`); la procedencia va en
   `x-flit-*` y en `contracts/ict/informe.md`. El **PR #698 de flit** ya tiene el contrato limpio (commit `90f24be`).
   Pendiente menor: ¿renombrar `info.title` «FLIT Core ICT API»?
2. **PR #76 de flit-gitops (borrador)**: https://github.com/flitsas/flit-gitops/pull/76, rama `atlas/qa-f4`, worktree
   `D:/FLIT/Code/flit-gitops-atlas-f4` (el checkout principal de gitops sigue en la rama ajena `docs/flito-pdn-argocd`).
   Trae atlas-docs (`replicas: 0` hasta sellar), MinIO propio `atlas-s3` (`replicas: 0`), NetworkPolicy, RabbitMQ con
   permisos mínimos por servicio, roles de base (`docs/host-177/atlas-qa-roles-base.md`), scripts
   `gen-atlas-qa-{migrador,rabbitmq,s3}.sh`, sitio nginx del S3 público y el runbook en `docs/atlas/`.
   - **S3 decidido (26/10, ATLAS-ADR-0051):** el de FLIT 2 de cada ambiente. QA = MinIO de `filemanager-qa`, bucket
     `atlas-qa`, usuarios `atlas-qa-api`/`atlas-qa-docs`; descargas por `https://qa.atlas.flitsas.com/atlas-qa/…` (nginx
     del host hacia `10.43.51.9:9000` conservando el Host). Ya no hay `REEMPLAZAR`. gitops `45d127f` quitó el MinIO propio.
     El service worker deja pasar `/atlas-<ambiente>/` (`501a6f6`).
   - pdn (Contabo) por verificar antes de la HU 4.11: las llaves de Contabo son de toda la cuenta (alcanzarían los adjuntos
     de FLIT 2), ciclo de vida, región para SigV4 y cómo reenviar `/atlas-pdn-eu2/` en la 169.
   - Dudas a verificar: que el MinIO de `filemanager-qa` traiga `mc` (el script se detiene si no); el JWKS por HTTP interno
     (OpenIddict exige HTTPS; solo afecta `POST /exportes`, la vía normal es la cola); medir memoria de atlas-docs.
   - **No volver a correr** `servicio-con-esquema-propio.sql` para atlas: deshace la separación de roles.

## 3. Orden para llevar la F4 a QA (detalle en el README de `services/atlas-qa` del PR #76)

1. ~~PR #31 integrado en `develop`~~ (26/10, `f635cce`).
   - 27/10: PR #76 de gitops integrado (00:26 UTC) y PR #32 `develop → staging` integrado (10:14 UTC), pero el CI y el CD de
     `staging` no arrancaron por facturación de la organización en GitHub (pago fallido o límite de gasto). Cuando un admin lo
     resuelva: confirmar el paso 2 (DBA) y relanzar `gh run rerun 40486905544` (cd) y `40486905524` (ci). Hasta entonces QA
     sigue con la versión anterior y atlas-docs en `ImagePullBackOff` (esperado).
   - 27/10, después: facturación arreglada, CD relanzado y **F4 corriendo en QA** (Argo Synced/Healthy, 5 pods 1/1,
     `/health/ready` Healthy). Cierre (paso 7) hecho: gitops #78 (`ef1ab00`, migrador solo con su cadena) y HSTS quitada
     del nginx del host (llega una sola vez, la de atlas-web). Falta el paso 8: una exportación de punta a punta.
2. **DBA:** pasos 1 a 5 de `docs/host-177/atlas-qa-roles-base.md` (incluye permisos puente para la versión actual y `pg_hba`).
3. nginx del host: copiar `docs/host-177/atlas-qa.nginx.conf` (trae `location /atlas-qa/`), `nginx -t` y recargar.
4. **Sellar** con los tres scripts (`gen-atlas-qa-s3.sh` además crea bucket, política y usuarios en el MinIO de
   `filemanager-qa`), agregar los archivos a `resources` y poner `replicas: 1` en atlas-docs, en el mismo PR #76.
5. Integrar el **PR #76** (reinicia RabbitMQ y atlas-api con la versión actual).
6. Abrir e integrar el PR `develop → staging` de la F4: el CD fija los cuatro digests y el migrador concede permisos.
   **Si la F4 llega a staging antes del paso 2, atlas-api queda no lista y QA se queda sin API.**
7. PR de cierre en gitops: quitar el `envFrom` de `atlas-api-secrets` del migrador y la HSTS del sitio del host.
8. Verificar en QA: portal con una organización de prueba, una exportación de punta a punta, cabeceras/CSP, límites.

**Ojo:** atlas-api ya **no arranca** en qa/pdn sin `Atlas__Proxy__RedesConfiables` (F4-SEC-13). Verificar también que el
nginx del host reemplace `X-Forwarded-For` en vez de encadenarlo.

## 4. Después (HU 4.11 · producción) — requiere visto bueno del líder

- Supuestos vigentes (el líder no los ha contradicho): `atlas.flitsas.com`, namespace propio `atlas-pdn` en la 169; sandbox
  en QA (`sandbox.qa.atlas.flitsas.online` solo como ejemplo, configurable); la revisión de seguridad la hizo Claude.
- Antes de abrir a externos en pdn (`docs/seguridad/revision-f4.md` §8): suite de la OpenID Foundation contra QA,
  `auth_time` en el registro de Entra, RBAC de `kubectl exec` (cuenta de emergencia), NetworkPolicy de atlas-docs,
  decidir si la cuenta de emergencia existe en pdn, alerta de Loki para `identidad.emergencia.*`.
- Invitar al cliente piloto (por definir: organización y paquete, por ejemplo ICT).

## 5. Decisiones del líder registradas

- **P-01:** `flitsas/flit` pasa a privado (aún figura PUBLIC al 24/10); trabajar como si ya lo fuera. Acceso de Actions de
  flit-atlas en `organization` y tag `v1` creados. Cuando flit sea privado, volver a correr el CI de los PR #697 y #698.
- ADR 0049 y 0050 aceptados (24/10). El spec compartido no lleva extensiones internas (lista blanca, ADR 0038).
- Recomendaciones aplicadas que el líder puede cambiar: Excel exportado conserva el morado `#4E00EB`; Word con sello de
  texto como marca de agua; tabla de visibilidad por rol externo del ADR 0043.

## 6. Pendientes abiertos (para preguntar o decidir)

- ICT: host de QA, autenticación del webhook, códigos de error no declarados en core-ict; excepción `breaking: advertencia`
  del PR #698.
- Guías: quién aprueba si un aplicativo no tiene responsables; si se exige aprobación de otra persona.
- Visor: CORS con `Origin: null` y los hosts de `ATLAS_VISOR_CONEXIONES`.
- Portal: URL públicas por contrato y ambiente (`Contratos:Portal:Servidores`); vigencia máxima al compartir con proveedores;
  si los empleados pueden previsualizar el portal como una organización.
- Dependencias con parche reciente (subir cuando cumplan 2 semanas): `dompurify` 3.4.17 (desde el 26/10), Pillow 13 y
  brotli 1.2.1 en atlas-docs; Go 1.26.x del CI (18 avisos de la librería estándar; atlas-render aún no se despliega).
- Bajos sin hacer de la revisión: F4-SEC-15, 16, 17, 20, 24, 27, 28, 31, 32; agrupación IPv6 /64 en los límites; límites
  de tasa en memoria por réplica.
- flit: lint de vacuum de los OpenAPI de FLIT 2 (core-api con 354 errores); AsyncAPI con `$ref` externos se publican
  empaquetados.
- Pasos del líder de fases anteriores: variable de organización `ATLAS_URL`, proteger ramas `develop`/`staging`/`release`,
  pasos de M365 (buzón compartido, RBAC de Exchange, flujo de Workflows, `Soporte__M365__Buzon`), alcance del equipo de
  soporte de Operaciones, importar responsables y Excel de soporte en QA, rotar el secreto de Entra expuesto el 22/10,
  activar el claim `auth_time`, aceptar los ADR 0020 a 0048 que siguen propuestos.
- Disco C: llegó a 0 MB libres (Docker se cayó). Hay ~12 GB en dos carpetas de Temp con workloads de .NET MAUI/Android.

## 7. Cómo retomar

1. Leer el prompt maestro y este archivo.
2. `git fetch && git checkout develop && git pull` (la rama `f4/portal-integracion` ya está integrada).
3. `gh run list --branch develop --limit 3`; `gh pr list -R flitsas/flit-gitops --author @me`.
4. `gh pr view 76 -R flitsas/flit-gitops` y `gh pr view 698 -R flitsas/flit` (sección 2).
5. Seguir la sección 3.

Notas operativas:
- El hook de pre-commit exige gitleaks en el PATH en formato POSIX: `PATH="/d/FLIT/tmp/gl-bin:$PATH" git commit -F <archivo>` (no `C:/` ni `$TEMP`).
- No correr la suite .NET y Vitest al mismo tiempo; Vitest falla por tiempo con la máquina cargada
  (`vitest run --no-file-parallelism` es estable).
- El E2E corre contra `ATLAS_API_URL=http://localhost:1 pnpm exec vite preview --port <n> --strictPort` con `ATLAS_E2E_URL`;
  detener solo ese proceso por su PID.
