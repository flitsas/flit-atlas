# Estado del trabajo · Flit Atlas

> **Corte:** 24/10/2026, noche (hora de Bogotá). Archivo vivo para retomar el trabajo en una sesión nueva.
> Se actualiza al cerrar cada jornada. El prompt maestro (`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`) sigue
> siendo la fuente que prevalece; aquí solo va **dónde quedamos** y **qué sigue**.

## 1. Dónde estamos

| Fase | Estado | Dónde |
|---|---|---|
| F0–F2 | Integradas en `develop` y desplegadas en QA | — |
| F3 · Contratos internos y legado ICT | Integrada en `develop` (PR #29) y en `staging` (PR #30, 24/10). El CD desplegó en QA (gitops `07e5eb1`, «atlas-qa: despliegue cf56f28») | Tag `v1` del workflow reutilizable sobre `b957751` |
| **F4 · Portal, exportes y pdn** | **Código completo hasta la ola 3 + correcciones de seguridad.** Falta la ola 4 (despliegue) | Rama `f4/portal-integracion`, **PR #31 en borrador** hacia `develop` (punta `9fbafee`) |

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

Última verificación local: `dotnet test Flit.Atlas.slnx` **788 de 789** (la omitida es la prueba de carga, que corre solo con
`ATLAS_CARGA_K6`). Web: Vitest 218/218 en serie, bundle inicial ~176,5 KB gzip. atlas-docs: pytest en verde (el PDF corre en
el contenedor con Pango y `ATLAS_DOCS_EXIGIR_PDF=1`). El CI del PR #31 sobre `9fbafee` estaba corriendo al cierre: revisarlo.

## 2. Trabajo en curso al cierre (ola 4)

Dos agentes quedaron trabajando. Si la sesión se cerró antes de que entregaran, **revisar si dejaron commit** y, si no,
relanzar la tarea con el mismo encargo:

1. **Limpieza de descripciones del contrato ICT** (encargo del líder). Rama local `f4/ict-descripciones` (worktree en
   `.claude/worktrees/`). El generador `services/docs/src/atlas_docs/ict` debe dejar las `description`/`summary` sin piezas
   internas (`services/core-ict`, «Feature #…», nombres de clases y archivos `.cs`, rutas `contracts/` de flit, comandos);
   la procedencia va a extensiones `x-flit-*` (la poda ya las quita) o a `contracts/ict/informe.md`. Regenerar con:
   `uv run python -m atlas_docs.ict --excel "<ruta del Excel en docs/atlas/insumos>" --base-flit "<ruta del repo flit>"`.
   Pruebas: pytest que recorre el spec y una prueba de poda del ICT real. Luego **actualizar el PR #698 de flit** con
   `contracts/ict/propuesta-flit/core-ict.v1.yaml`.
2. **PR en borrador a flit-gitops para QA** (rama `atlas/qa-f4`, worktree aparte en `D:\FLIT\Code\flit-gitops-atlas-f4`;
   el checkout principal de gitops está en una rama ajena `docs/flito-pdn-argocd` y **no se toca**). Debe traer:
   atlas-docs (Deployment, Service, NetworkPolicy, variables `ATLAS_DOCS_*`), `Atlas__Proxy__RedesConfiables`,
   `Plataforma__Almacenamiento__*` con el host público del S3, `ATLAS_S3_PUBLICO` y `ATLAS_HSTS` en atlas-web, usuarios de
   RabbitMQ por servicio, CORS y ciclo de vida del bucket, roles de base (SealedSecret `atlas-migrador-secrets` y SQL del
   DBA), runbook copiado a gitops. Microcks **no** se despliega todavía.

## 3. Orden para llevar la F4 a QA

1. Integrar la limpieza del ICT en `f4/portal-integracion`, esperar el CI del PR #31 en verde y marcarlo **listo**.
2. El líder integra el **PR #31** en `develop`.
3. El DBA separa los roles de la base en QA (runbook §3): `flit_atlas_migrador` dueño del esquema; `flit_atlas` sin
   propiedad, sin `BYPASSRLS`. **Sin esto `/health/ready` deja a atlas-api no lista.**
4. Generar y sellar los secretos nuevos con los scripts `docs/host-177/gen-atlas-qa-*.sh` del PR de gitops.
5. Integrar el **PR de gitops** (declara la imagen `atlas-docs`; sin él el CD de staging falla al fijar digests).
6. Abrir y integrar el PR `develop → staging` de la F4. Verificar en QA: portal con una organización de prueba, una
   exportación de punta a punta, cabeceras/CSP, límites de tasa.

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
2. `git fetch && git checkout f4/portal-integracion && git pull`.
3. `gh pr view 31` y `gh pr checks 31`; `gh pr list -R flitsas/flit-gitops --author @me`.
4. Revisar las ramas locales `f4/ict-descripciones` y, en gitops, `atlas/qa-f4` (sección 2).
5. Seguir la sección 3.

Notas operativas:
- El hook de pre-commit exige gitleaks en el PATH con formato `/c/...` (no `C:/` ni `$TEMP`).
- No correr la suite .NET y Vitest al mismo tiempo; Vitest falla por tiempo con la máquina cargada
  (`vitest run --no-file-parallelism` es estable).
- El E2E corre contra `ATLAS_API_URL=http://localhost:1 pnpm exec vite preview --port <n> --strictPort` con `ATLAS_E2E_URL`;
  detener solo ese proceso por su PID.
