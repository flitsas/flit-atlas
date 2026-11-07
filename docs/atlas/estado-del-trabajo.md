# Estado del trabajo · Flit Atlas

> **Corte:** 06/11/2026 (hora de Bogotá). Archivo vivo para retomar el trabajo en una sesión nueva.
> Se actualiza al cerrar cada jornada. El prompt maestro (`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`) sigue
> siendo la fuente que prevalece; aquí solo va **dónde quedamos** y **qué sigue**.

## 1. Dónde estamos

**El MVP (F0–F4) está en QA y en pdn con la versión de `release`** (primer CD de `release` el 04/11). Lo que falta es
cerrar los pendientes de seguridad antes de abrir a externos e invitar al cliente piloto.

| Fase | Estado | Dónde |
|---|---|---|
| F0–F3 | Integradas y desplegadas en QA | — |
| F4 · Portal, exportes y pdn | Integrada en `develop`, `staging` y `release` | QA: `qa.atlas.flitsas.com`; pdn: `atlas.flitsas.com` |
| HU 4.11 · pdn | Encendido el 28/10 en la **145**; **primer CD de `release` el 04/11** (PR #43, gitops `09a425d`) | `services/atlas-pdn/` de flit-gitops |
| Bajos de la revisión de seguridad | PR #39 y #40 (F4-SEC-15, 16, 17, 20, 24, 27, 28, 31, 32) integrados el 03/11 y desplegados en QA y pdn | — |
| Publicación de contratos | ATLAS-ADR-0052 (PR #44 y #45): Atlas central en pdn y publicación de prueba en QA desde `staging` | Variables de organización `ATLAS_URL` y `ATLAS_URL_QA` |

La tabla de HU y ADR de la F4 (4.1 a 4.10, ADR 0038 y 0041 a 0051) está en el historial de este archivo (corte del
28/10) y en `docs/adr/README.md`.

### Decisiones de pdn (28/10/2026, ATLAS-ADR-0019 y 0051 actualizados el 03/11)

- Servidor: la **145** (donde ya apuntaba el DNS), no la 169. Argo lee `clusters/vps-145/atlas-pdn.yaml` (solo
  `root-vps-145`; nunca `apps/`).
- S3: **MinIO propio** en `atlas-pdn`, bucket `atlas-pdn`, reenvío `/atlas-pdn/` en el nginx de la 145. No Contabo (sus
  llaves son de toda la cuenta).
- Postgres: base `atlas` propia por PgBouncer (`pool_mode=session`), roles separados como en QA.
- Entra: pdn **reusa el registro de QA** (con el redirect de pdn). Rotar su secreto afecta a los dos ambientes.
- Abierto solo a empleados hasta invitar al cliente piloto. Paquetes solo con versiones estables (en QA, también `-rc`).

### Verificado el 04/11 y 05/11

- pdn con la versión de `release`: `/health/ready` 200 y `/api/v1/identidad/cuenta/seguridad` 401 (antes 404).
- Contratos: `atlas` y `atlas-eventos` 1.0.0 en el canal pdn y `1.0.0-rc.f1f2128` en el canal qa del Atlas central;
  `contratos-qa` publicó la misma rc en el Atlas de QA (CD de `staging` 41944015465).
- Arranque de pdn: el líder activó la cuenta de emergencia (`admin.emergencia@atlas.local`) y se asignó
  `atlas.superadmin` de forma **manual** a su persona (sin acceso a Entra para el mapeo de grupo).

### Verificado en QA el 05/11 (pdn queda por verificar)

- NetworkPolicy de atlas-docs activa: desde atlas-web la conexión a `:5035` se rechaza (el host sí recibe 200) y atlas-docs
  no sale a internet, pero sí llega a RabbitMQ.
- `atlas-qa` no tiene Role ni RoleBinding, y `cluster-admin` solo lo tienen `system:masters` y las cuentas de helm. El
  kubeconfig es solo de root: `kubectl exec` lo limita quién tiene root en el host, no un RBAC del namespace.

### Verificado el 03/11 desde fuera

- Cabeceras en QA y pdn: CSP estricta (sin `unsafe-inline`), Trusted Types en Report-Only, HSTS una sola vez, COOP/CORP,
  `nosniff`, `Referrer-Policy`, `Permissions-Policy`.
- Límites en QA: 130 peticiones anónimas a `/.well-known/openid-configuration` en un minuto → 120 × 200 y 10 × 429 con
  `Retry-After: 60`.
- `Plataforma__Publicacion__Audiencia` no está definida en QA ni en pdn: la regla nueva del PR #40 (F4-SEC-27) no
  impide que arranquen.

## 2. Qué sigue (en orden)

**Decisión del líder del 06/11:** externos, Entra, seguridad y todo lo que dependa de él van al final. Lo demás se
implementa de forma autónoma y se despliega en QA y pdn.

Tres PR listos para revisión, apilados y con el CI completo en verde; **ninguno está integrado ni desplegado**. Se integran en
este orden porque cada uno se construye sobre el anterior:

| PR | Fase | ADR | Último commit |
|---|---|---|---|
| #47 | F5: documentación viva (sin compartir, editor visual ni HU 5.7) | 0053 | `286138d` |
| #48 | F6: lienzo, Mermaid, BPMN y export SVG (HU 6.6, 6.7 y 6.8) | 0054 | `d2146c5` |
| #49 | F8 sin LiteLLM (HU 8.1, 8.2, 8.3, 8.5 y tablero de 8.6) | 0055 | `a24c3bb` |

1. **Integra el líder (decisión del 06/11).** `develop`, `staging` y `release` exigen una aprobación, incluso a administradores,
   y GitHub no deja aprobar el PR propio. Hace falta que apruebe otro administrador, o que el líder quite `enforce_admins`
   un momento y lo vuelva a activar.
2. **Al desplegar:**
   - `atlas-render` en flit-gitops: PR #109 (QA) justo antes de pasar `develop` a `staging`, y PR #110 (pdn) justo antes de pasar
     `staging` a `release`. Después, `rollout restart` de atlas-api. Hoy no está desplegado, así que el diff de contratos
     (HU 3.5) probablemente no funciona en QA ni en pdn (sin comprobar en los servidores).
   - atlas-api y atlas-docs van juntos: con un atlas-docs viejo, exportar documentos falla por tiempo a las 2 horas.
   - Mover el tag `v1` a un commit con `atlas-publicar-docs.yml`, para publicar documentos desde otros repos.
   - El migrador carga los 13 casos y las 7 políticas de la F8 aunque `modulo.gia` siga apagado. El líder aprobó que vayan a pdn.
3. **F6 restante:** los modelos LikeC4 (HU 6.2) están en borrador en flit #807 y flito #750, con CI en verde; se integra primero
   #807. No lanzar `pr-monitor-agent` sobre #750. Las HU 6.3 a 6.5 y 6.9 van en un PR aparte de flit-atlas, en construcción.
   HU 6.1 se coordina con el líder.
4. **F-IA y F7:** F-IA requiere aprobar su ADR y definir P-04 y P-08. F7 depende de F-IA y no se construye antes, porque no
   se podría probar. La HU 5.7 y la 8.4 esperan también al gateway.
5. **Tiptap:** se puede evaluar desde el 19/11 para el editor visual de la HU 5.3 (hoy, `<textarea>` con vista previa).

Pendientes de esos PR para preguntar:

- F8: quórum del Comité (hoy un voto basta), quien tiene `atlas.gia.gestionar` puede darse ese rol, y si evaluar y aprobar
  puede hacerlo la misma persona.
- Licencias: `elkjs` (EPL-2.0 o GPL-3.0) y D2 (MPL-2.0).
- Sin probar en ninguno: la PWA contra atlas-api real, WebSocket real detrás del nginx, migraciones en QA o pdn, Lighthouse
  y dispositivos reales. Los E2E de `shell`, `sesion`, `accesibilidad` e `invitaciones` necesitan atlas-api.
- Contraste: `documentacion/componentes.tsx` tiene el mismo aviso de error (4,08) que se corrigió en diagramas.
- `ClienteRenderDiff` no captura las excepciones de Polly (sin probar).
- Públicos: `docs/atlas/00-plan-maestro-plataforma-central.md` y `02-plan-gobernanza-ia.md` nombran credenciales versionadas
  en repos legados, y `flit-atlas` sigue público. Lo mismo resume la semilla de la F8.

**Para el final:**

4. **Verificación de pdn desde la 145** (README de `services/atlas-pdn`, sección «Verificación»): Argo Synced/Healthy,
   pods 1/1, permisos del migrador, IP real en `identidad_evento_seguridad.ip` tras un ingreso con Microsoft, una
   exportación con descarga por `/atlas-pdn/` y los eventos `identidad.emergencia.*` del arranque en la auditoría.
   Incluye comprobar en pdn la NetworkPolicy de atlas-docs y quién puede usar `kubectl exec`.
5. **Alerta de `identidad.emergencia.*` en pdn:** hay trabajo sin commitear en la rama `fix/alerta-emergencia-correo`
   (worktree `D:\aalerta`), con un aviso por correo. Falta decidir el mecanismo (ADR).
6. **Prueba de navegador en QA:** el clic de «Continuar» de `/connect/continuar` en Chrome, Edge, Firefox y Safari
   (cookie `SameSite=Strict`, F4-SEC-20).
7. **Entra:** rotar el secreto del cliente (compartido por QA y pdn; se expuso de nuevo el 05/11): crear uno nuevo,
   sellarlo en los Secret de `atlas-qa` y `atlas-pdn` de flit-gitops, `rollout restart` de atlas-api en los dos, probar el
   ingreso con Microsoft y borrar el viejo. Mapear el grupo de administración a `atlas.superadmin` y decidir si se quita
   la asignación manual; activar el claim `auth_time`.
8. **Externos en QA:** «Seguridad de la cuenta» con un externo; portal con una organización de prueba (crear la
   organización, compartir un paquete, invitar a una cuenta de prueba y entrar como cliente); suite de la OpenID
   Foundation.
9. Volver privados `flit` y `flit-atlas`.
10. Invitar al cliente piloto (organización y paquete por definir, por ejemplo ICT).

Hecho desde el corte anterior: el tag `v1` del workflow de publicación apunta a `95e537e` (merge del PR #43) y los tags
`v*` tienen ruleset activo desde el 05/11.

## 3. Decisiones del líder registradas

- **P-01:** `flitsas/flit` y `flitsas/flit-atlas` siguen **públicos** al 03/11; el plan era volverlos privados ~3/11.
  Al hacerlo, relanzar el CI de flit que publica en Atlas (con los dos privados el workflow reutilizable sigue sirviendo).
  flit-gitops ya es privado.
- ADR 0049 y 0050 aceptados (24/10). El spec compartido no lleva extensiones internas (lista blanca, ADR 0038).
- 28/10: pdn en la 145, MinIO propio, encender solo para empleados, QA acepta `-rc` en paquetes (ADR 0041).
- 04/11: `ATLAS_URL` de la organización = Atlas central de pdn; `ATLAS_URL_QA` para la prueba desde `staging`
  (ATLAS-ADR-0052). No se separa la publicación por ambiente: el Consolidado necesita todos los canales en un Atlas.
- 05/11: cuenta de emergencia activada en pdn; superadmin asignado a mano al líder hasta tener el mapeo de grupo.
- 06/11: externos, Entra, seguridad y lo que dependa del líder van al final; lo demás, autónomo hasta QA y pdn.
- 06/11: integra el líder; se autorizan los PR de LikeC4 en `flit` y `flito`; la semilla de la F8 va a pdn; el rol de líder
  técnico ve `restringido`; todos los empleados leen y aceptan las políticas de IA vigentes (`atlas.gia.politicas.ver`).
- Excepción de breaking de core-ict 1.1.0 aceptada (ADR 0037); se quita cuando la 1.1.0 llegue a `release`.
- Recomendaciones aplicadas que el líder puede cambiar: Excel exportado con el morado `#4E00EB`; Word con sello de texto
  como marca de agua; tabla de visibilidad por rol externo del ADR 0043.

## 4. Pendientes abiertos (para preguntar o decidir)

- Del PR #39/#40: enlaces en lugar de botones en el consentimiento (F4-SEC-20, por la CSP); restaurar el reparto con
  personas inactivas (hoy se permite); HIBP caído falla abierto contra la lista local de 10.000 (¿alerta?); ids de repos
  fijados por configuración o en la base; quitar passkey, cambiar TOTP y reautenticar en la página quedan fuera.
- ICT: host de QA, autenticación del webhook, códigos de error no declarados en core-ict; ¿renombrar `info.title`
  «FLIT Core ICT API»?
- Guías: quién aprueba si un aplicativo no tiene responsables; si se exige aprobación de otra persona.
- Visor: CORS con `Origin: null` y los hosts de `ATLAS_VISOR_CONEXIONES`.
- Portal: URL públicas por contrato y ambiente (`Contratos:Portal:Servidores`, vacías en pdn); vigencia máxima al
  compartir con proveedores; si los empleados pueden previsualizar el portal como una organización.
- Sandbox en pdn deshabilitado (`Contratos__Sandbox__TrabajoHabilitado: false`) hasta decidir el despliegue de Microcks.
- Dependencias: `brotli`/`brotlicffi` 1.2.1 desde ~07/11; `dompurify` 3.4.17 desde ~09/11; revisar los otros topes del
  24/10 en `services/docs/pyproject.toml`.
- Bajos que siguen sin hacer: agrupación IPv6 /64 en los límites; límites de tasa en memoria por réplica.
- flit: lint de vacuum de los OpenAPI de FLIT 2 (core-api con 354 errores); AsyncAPI con `$ref` externos se publican
  empaquetados.
- Pasos del líder de fases anteriores: proteger ramas `develop`/`staging`/`release`, pasos de M365 (buzón compartido,
  RBAC de Exchange, flujo de Workflows, `Soporte__M365__Buzon`), alcance del equipo de soporte de Operaciones, importar
  responsables y Excel de soporte en QA, aceptar los ADR 0020 a 0048 que siguen propuestos.
- Medir la CPU real de `atlas-pdn` en la 145 (va con requests mínimos y sin límite).

## 5. Cómo retomar

1. Leer el prompt maestro y este archivo.
2. `git fetch && git checkout develop && git pull`.
3. `gh pr list`; `gh run list --branch develop --limit 3`; `gh pr list -R flitsas/flit-gitops --author @me`.
4. Seguir la sección 2.

Notas operativas:
- El hook de pre-commit exige gitleaks en el PATH en formato POSIX: `PATH="/d/FLIT/tmp/gl-bin:$PATH" git commit -F <archivo>` (no `C:/` ni `$TEMP`).
- Las pruebas .NET usan Microsoft.Testing.Platform: filtrar con
  `dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class '*Clase'` (`--filter` no aplica).
- No correr la suite .NET y Vitest al mismo tiempo; Vitest falla por tiempo con la máquina cargada
  (`vitest run --no-file-parallelism` es estable).
- El E2E corre contra `ATLAS_API_URL=http://localhost:1 pnpm exec vite preview --port <n> --strictPort` con `ATLAS_E2E_URL`;
  detener solo ese proceso por su PID.
- **No volver a correr** `servicio-con-esquema-propio.sql` para atlas: deshace la separación de roles.
- atlas-api no arranca en qa/pdn sin `Atlas__Proxy__RedesConfiables` (F4-SEC-13).
