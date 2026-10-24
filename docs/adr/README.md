# ADR de Flit Atlas

Decisiones de arquitectura de Atlas, en formato **MADR 4** y con la serie propia `ATLAS-ADR-NNNN` ([ATLAS-ADR-0001](0001-registro-de-decisiones-madr.md)). Para crear uno nuevo, copia la [plantilla](plantilla.md), toma el siguiente número libre y agrégalo a este índice en el mismo PR.

| Id | Título | Estado | Fuente |
|---|---|---|---|
| [ATLAS-ADR-0001](0001-registro-de-decisiones-madr.md) | Registro de decisiones con MADR y serie propia ATLAS-ADR | aceptado | §1 regla 2 |
| [ATLAS-ADR-0002](0002-repo-privado-flit-atlas.md) | Repo privado propio flitsas/flit-atlas | aceptado | A-01 |
| [ATLAS-ADR-0003](0003-stack-base.md) | Stack base: PWA React + Vite, API .NET 10, servicios Go y Python | aceptado | A-02 |
| [ATLAS-ADR-0004](0004-identidad-propia.md) | Identidad propia: ASP.NET Core Identity, Entra ID, invitaciones y OpenIddict | aceptado | A-03, §6 |
| [ATLAS-ADR-0005](0005-diagramas-likec4-y-lienzo.md) | Diagramas: LikeC4, lienzo propio, Mermaid, BPMN y D2 | aceptado | A-04 |
| [ATLAS-ADR-0006](0006-despliegue-k3s-gitops.md) | Despliegue en k3s con flit-gitops y Argo CD | aceptado | A-05 |
| [ATLAS-ADR-0007](0007-persistencia.md) | Persistencia: esquema atlas con prefijos, Redis, S3 y RabbitMQ | aceptado | A-06 |
| [ATLAS-ADR-0008](0008-bff-y-entrada-http-propia.md) | BFF en atlas-api y entrada HTTP propia | aceptado | A-07 |
| [ATLAS-ADR-0009](0009-publicacion-ci-oidc-github.md) | Publicación desde CI con OIDC de GitHub y lista blanca | aceptado | A-08 |
| [ATLAS-ADR-0010](0010-validacion-contratos-oasdiff-vacuum.md) | Validación de contratos con oasdiff y vacuum | aceptado | A-09 |
| [ATLAS-ADR-0011](0011-ia-litellm-y-atlas-ia.md) | IA a través de LiteLLM y orquestador en atlas-ia | aceptado | A-10 |
| [ATLAS-ADR-0012](0012-alcance-mvp.md) | Alcance del MVP: fases F0 a F4 | aceptado | A-11 |
| [ATLAS-ADR-0013](0013-nombre-y-hosts.md) | Nombre «Flit Atlas» y hosts por ambiente | aceptado (los hosts, reemplazados por ATLAS-ADR-0019) | A-12 |
| [ATLAS-ADR-0014](0014-mensajeria-outbox.md) | Mensajería: outbox e inbox del SDK de plataforma sobre RabbitMQ | reemplazado por ATLAS-ADR-0018 | F0.2 |
| [ATLAS-ADR-0015](0015-paquetes-compartidos-de-flit.md) | Paquetes compartidos de flit: NuGet del SDK y réplica de tokens de marca | aceptado (la parte NuGet, reemplazada por ATLAS-ADR-0018) | F0.2 |
| [ATLAS-ADR-0016](0016-contrato-de-modulo.md) | Contrato de módulo: IModuloAtlas y ModuloAtlas con banderas | aceptado | §10 |
| [ATLAS-ADR-0017](0017-aprobacion-del-lider-tecnico.md) | El líder técnico puede integrar sus propios PR sin una segunda aprobación | aceptado | ATLAS-ADR-0002 |
| [ATLAS-ADR-0018](0018-outbox-propio-y-broker-por-ambiente.md) | Outbox e inbox propios de Atlas y RabbitMQ propio por ambiente | aceptado | N-04, N-08 |
| [ATLAS-ADR-0019](0019-ambientes-qa-y-pdn.md) | Solo dos ambientes desplegados (qa y pdn) con hosts bajo flitsas.com | aceptado | N-01, N-02 |
| [ATLAS-ADR-0020](0020-shell-pwa-ui-banderas-y-web-push.md) | Shell PWA: Tailwind con tokens FLIT y componentes compatibles con la CSP, banderas de módulo y Web Push | propuesto | §10, §11, HU 1.6 |
| [ATLAS-ADR-0021](0021-contratos-propios-openapi-y-asyncapi.md) | Contratos propios: OpenAPI generado desde el código y AsyncAPI atado al catálogo de eventos | propuesto | A-09, HU 1.7 |
| [ATLAS-ADR-0022](0022-sesiones-y-seguridad-de-identidad.md) | Sesiones y seguridad de la identidad: sello de seguridad, reautenticación, bloqueo progresivo, límite de tasa y eventos de seguridad | propuesto | A-03, §6.3, HU 1.3b |
| [ATLAS-ADR-0023](0023-modelo-de-roles-y-permisos.md) | Modelo de roles y permisos: manifiestos en el código, permisos calculados en cada petición, predicado obligatorio y RLS | propuesto | §7, §9, §12, HU 1.4 |
| [ATLAS-ADR-0024](0024-servidor-de-tokens-openiddict.md) | Servidor de tokens: OpenIddict embebido, clientes por configuración, scopes por módulo, llaves en certificados montados y conformidad con pruebas propias | propuesto | A-03, §6.3, HU 1.3c |
| [ATLAS-ADR-0025](0025-organizaciones-invitaciones-y-cuentas-externas.md) | Organizaciones, invitaciones y cuentas locales de externos: enlace de un solo uso, passkeys de Identity, TOTP, recuperación, términos versionados y correo por Graph o SMTP | propuesto | §6.2, §6.3, §7.3, HU 1.5 |
| [ATLAS-ADR-0026](0026-registro-de-modulos-y-composicion.md) | Registro de módulos: IModuloAtlas en Plataforma, lista única en Flit.Atlas.Composicion y bandera que hace cumplir el servidor | propuesto | §10, HU 1.9 |
| [ATLAS-ADR-0027](0027-cuenta-de-administrador-de-emergencia.md) | Cuenta de administrador de emergencia: cuenta local con superadmin, sin contraseña por defecto, activada con un comando de consola y un enlace de un solo uso | aceptado (decisión del líder, 23/10/2026) | A-03, §6, §7.2 |
| [ATLAS-ADR-0028](0028-catalogo-de-aplicativos.md) | Catálogo de aplicativos: jerarquía en una tabla con padre, permiso de edición por aplicativo, semilla idempotente en el migrador y ficha con pestañas que aportan los módulos | propuesto | §8.1, §9, HU 2.1 |
| [ATLAS-ADR-0029](0029-soporte-de-los-sabados.md) | Soporte de los sábados: módulo propio con equipos por área, turnos por persona, festivos de Colombia calculados en Atlas y generación equitativa determinista con vista previa y huella | propuesto | §8.3, §9, HU 2.6, HU 2.7, HU 2.8, HU 2.9, HU 2.11 |
| [ATLAS-ADR-0030](0030-sincronizacion-de-personas-con-graph.md) | Sincronización de personas con Microsoft Graph: trabajo diario con candado, origen configurable (enlazadas por defecto), inactivación con cierre de sesiones, foto en la base con caché y directorio interno | propuesto | §6.1, §8.2, HU 2.2 |
| [ATLAS-ADR-0031](0031-responsables-y-rol-implicito.md) | Responsables y rol implícito: módulo de equipo con reparto por aplicativo del catálogo, importación del documento con vista previa, historial por fotos con huella, rol implícito de editor por aplicativo y lecturas entre módulos en Plataforma | propuesto | §7.2, §8.2, HU 2.3, HU 2.4, HU 2.5 |
| [ATLAS-ADR-0032](0032-acciones-sin-conexion-con-llave-de-idempotencia.md) | Acciones sin conexión: cola en IndexedDB del dispositivo y llave de idempotencia por acción que el servidor guarda con la acción | propuesto | §11, HU 2.9 |
| [ATLAS-ADR-0033](0033-soporte-en-microsoft-365.md) | Soporte en Microsoft 365: calendario compartido por Graph con marca de pendiente en la transacción y trabajo con reintentos, aviso en Teams por un flujo de Workflows y feeds .ics con token guardado como hash | propuesto | §8.3, §8.9, §9, HU 2.10 |
| [ATLAS-ADR-0034](0034-datos-sin-conexion.md) | Datos sin conexión: respuestas de la API guardadas en IndexedDB por recurso, con permiso y TTL, sin teléfonos y de una sola persona por dispositivo | propuesto | §11, §12, HU 2.12 |
| [ATLAS-ADR-0035](0035-publicacion-de-contratos.md) | Publicación de contratos: token OIDC de GitHub Actions validado en Plataforma, lista blanca en el catálogo, versiones inmutables en el módulo de contratos, workflow reutilizable atlas-publicar, canales por ambiente y Consolidado | propuesto | A-08, A-09, §8.4, §9, HU 3.1, HU 3.2, HU 3.3, HU 3.4 |
| [ATLAS-ADR-0036](0036-diff-de-contratos-en-render.md) | Diff de contratos en atlas-render: oasdiff como librería con la configuración de la CLI, caché LRU en memoria por par de SHA-256 y cliente tipado IRenderDiff en Plataforma | propuesto | A-09, §8.4, HU 3.5 |
| [ATLAS-ADR-0037](0037-enriquecimiento-contrato-ict-flit2.md) | Enriquecimiento del contrato de ICT de FLIT 2 con el Excel: core-ict.v1.yaml como base, un solo OpenAPI 3.1 con URL base única por ambiente en contracts/ict/ y reglas de vacuum para lo compartible | propuesto | §8.4, HU 3.8 |
| [ATLAS-ADR-0038](0038-poda-de-specs.md) | Poda de specs: recorrido genérico del árbol JSON para OpenAPI 3.0/3.1 y AsyncAPI 2.x/3.x, cierre transitivo por componentes, fail-closed con verificación final y pruebas de propiedades sobre specs reales | propuesto | §8.4, plan 00 §6.2 y §8.6, HU 4.2 |
| [ATLAS-ADR-0039](0039-vista-de-contratos.md) | Vista de contratos: Scalar en un iframe aislado con CSP propia, vista propia de operaciones, AsyncAPI y proto, ejemplos con httpsnippet y diff desde atlas-render | propuesto | A-02, A-09, §8.4, §11, §12, HU 3.6, HU 3.9 |
| [ATLAS-ADR-0040](0040-guias-de-contratos.md) | Guías de contratos: revisiones inmutables con huella, publicación que aprueba el responsable, Markdown sin HTML sanitizado en la PWA y diff de revisiones en el cliente | propuesto | §7.2, §8.4, HU 3.7 |
| [ATLAS-ADR-0041](0041-paquetes-de-contratos.md) | Paquetes de contratos: revisiones inmutables con huella, revisión vigente que se publica o se fija, reglas revisadas al guardar y al publicar, rol implícito sobre todos sus contratos y visibilidad declarada en el spec | propuesto | §7.2, §8.4, §8.9, HU 4.1 |
| [ATLAS-ADR-0042](0042-exportaciones-de-paquetes.md) | Exportaciones de paquetes: generador puro en atlas-docs (Jinja2, WeasyPrint, python-docx, openpyxl, Postman propio) con salida determinista y hash del contenido; de punta a punta por la cola de RabbitMQ con la entrada en el S3, URL firmada de 15 minutos, retención de 7 días, auditoría y aviso; compartir desde el móvil con la hoja nativa y solo enlaces internos | propuesto | A-02, §5.1, §6.3, §8.5, §11, HU 4.5, HU 4.6 |
| [ATLAS-ADR-0043](0043-comparticiones-y-portal.md) | Comparticiones y portal de clientes: contratos_comparticion con RLS como única puerta, acceso calculado en cada petición por organización, alcance de la invitación y rol, spec podado con caché y pruebas de aislamiento que se exigen por endpoint | propuesto | §6.2, §7.3, §8.4, §8.9, §12, HU 4.3, HU 4.4 |
| [ATLAS-ADR-0044](0044-autenticacion-entre-servicios.md) | Autenticación entre servicios: usuario de RabbitMQ por servicio para la cola y token client_credentials de OpenIddict, firmado sin cifrar con la audiencia del servicio y validado con el JWKS, para las llamadas HTTP internas | propuesto | A-03, §5.1, §6, HU 4.5 |
| [ATLAS-ADR-0048](0048-endurecimiento-f4.md) | Endurecimiento de F4: CSP con informe de violaciones y Trusted Types en modo informe, cabeceras de seguridad en la API y en atlas-web, límites de tasa por persona, organización, IP y llave de sandbox, y tope de exportaciones en curso | propuesto | §6, §12, §13, §15, HU 4.10 |

Las fuentes `A-xx` y `§x` remiten a [`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`](../atlas/prompts/00-prompt-maestro-flit-atlas.md).
