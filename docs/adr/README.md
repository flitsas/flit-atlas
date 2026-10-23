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

Las fuentes `A-xx` y `§x` remiten a [`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`](../atlas/prompts/00-prompt-maestro-flit-atlas.md).
