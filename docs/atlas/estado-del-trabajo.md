# Estado del trabajo · Flit Atlas

> **Corte:** 03/11/2026 (hora de Bogotá). Archivo vivo para retomar el trabajo en una sesión nueva.
> Se actualiza al cerrar cada jornada. El prompt maestro (`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`) sigue
> siendo la fuente que prevalece; aquí solo va **dónde quedamos** y **qué sigue**.

## 1. Dónde estamos

**El MVP (F0–F4) está construido y corriendo en QA y en pdn.** Lo que falta es formalizar la entrega a `release`,
cerrar los pendientes de seguridad antes de abrir a externos e invitar al cliente piloto.

| Fase | Estado | Dónde |
|---|---|---|
| F0–F3 | Integradas y desplegadas en QA | — |
| F4 · Portal, exportes y pdn | Código completo (PR #31, #33, #35, #37) en `develop` y `staging`; CD de `staging` en verde el 28/10 (PR #38) | QA: `qa.atlas.flitsas.com` |
| HU 4.11 · pdn | **Encendido el 28/10** en la **145** (flit-gitops #85 y #86): `atlas.flitsas.com` responde `/health/ready` 200, con los digests de QA hasta el primer CD de `release` | `services/atlas-pdn/` de flit-gitops |
| Bajos de la revisión de seguridad | PR #39 (F4-SEC-15, 16, 20, 24) y PR #40 apilado (F4-SEC-17, 27, 28, 31, 32), abiertos el 03/11 | Esperan revisión del líder |

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

### Verificado el 03/11 desde fuera

- Cabeceras en QA y pdn: CSP estricta (sin `unsafe-inline`), Trusted Types en Report-Only, HSTS una sola vez, COOP/CORP,
  `nosniff`, `Referrer-Policy`, `Permissions-Policy`.
- Límites en QA: 130 peticiones anónimas a `/.well-known/openid-configuration` en un minuto → 120 × 200 y 10 × 429 con
  `Retry-After: 60`.
- `Plataforma__Publicacion__Audiencia` no está definida en QA ni en pdn: la regla nueva del PR #40 (F4-SEC-27) no
  impide que arranquen.

## 2. Qué sigue (en orden)

1. **Revisar e integrar el PR #39 y luego el #40** en `develop` (el #40 cambia de base solo). Después, `develop → staging`
   para que lleguen a QA. Al integrar el #40 en QA, probar «Seguridad de la cuenta» con un externo y el clic de
   «Continuar» de `/connect/continuar` en Chrome, Edge, Firefox y Safari (cookie `SameSite=Strict`, F4-SEC-20).
2. **Primer CD de `release`**: PR `staging → release` (hoy `release` está en el commit de la F0). El CD fija los digests
   de producción en `services/atlas-pdn` de gitops. Antes, confirmar que `release` tiene las protecciones y el entorno
   del CD.
3. **Verificación de pdn desde la 145** (README de `services/atlas-pdn`, sección «Verificación»): Argo Synced/Healthy,
   pods 1/1, permisos del migrador, IP real en `identidad_evento_seguridad.ip` tras un ingreso con Microsoft, y una
   exportación con descarga por `/atlas-pdn/`.
4. **QA:** portal con una organización de prueba (las `-rc` ya se pueden empaquetar): crear la organización, compartir un
   paquete, invitar a una cuenta de prueba y entrar como cliente.
5. **Antes de abrir a externos en pdn** (`docs/seguridad/revision-f4.md` §8): suite de la OpenID Foundation contra QA,
   `auth_time` en el registro de Entra, RBAC de `kubectl exec` (cuenta de emergencia), NetworkPolicy de atlas-docs,
   decidir si la cuenta de emergencia existe en pdn, alerta de Loki para `identidad.emergencia.*`, proteger los tags `v*`.
6. Invitar al cliente piloto (organización y paquete por definir, por ejemplo ICT).

## 3. Decisiones del líder registradas

- **P-01:** `flitsas/flit` y `flitsas/flit-atlas` siguen **públicos** al 03/11; el plan era volverlos privados ~3/11.
  Al hacerlo, relanzar el CI de flit que publica en Atlas (con los dos privados el workflow reutilizable sigue sirviendo).
  flit-gitops ya es privado.
- ADR 0049 y 0050 aceptados (24/10). El spec compartido no lleva extensiones internas (lista blanca, ADR 0038).
- 28/10: pdn en la 145, MinIO propio, encender solo para empleados, QA acepta `-rc` en paquetes (ADR 0041).
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
  responsables y Excel de soporte en QA, **rotar el secreto de Entra expuesto el 22/10** (ahora lo comparten QA y pdn),
  activar el claim `auth_time`, aceptar los ADR 0020 a 0048 que siguen propuestos.
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
