# ADR de Flit Atlas

Decisiones de arquitectura de Atlas, en formato **MADR 4** y con la serie propia `ATLAS-ADR-NNNN` ([ATLAS-ADR-0001](0001-registro-de-decisiones-madr.md)). Para crear uno nuevo, copia la [plantilla](plantilla.md), toma el siguiente número libre y agrégalo a este índice en el mismo PR.

| Id | Título | Estado | Fuente |
|---|---|---|---|
| [ATLAS-ADR-0001](0001-registro-de-decisiones-madr.md) | Registro de decisiones con MADR y serie propia ATLAS-ADR | propuesto | §1 regla 2 |
| [ATLAS-ADR-0002](0002-repo-privado-flit-atlas.md) | Repo privado propio flitsas/flit-atlas | propuesto | A-01 |
| [ATLAS-ADR-0003](0003-stack-base.md) | Stack base: PWA React + Vite, API .NET 10, servicios Go y Python | propuesto | A-02 |
| [ATLAS-ADR-0004](0004-identidad-propia.md) | Identidad propia: ASP.NET Core Identity, Entra ID, invitaciones y OpenIddict | propuesto | A-03, §6 |
| [ATLAS-ADR-0005](0005-diagramas-likec4-y-lienzo.md) | Diagramas: LikeC4, lienzo propio, Mermaid, BPMN y D2 | propuesto | A-04 |
| [ATLAS-ADR-0006](0006-despliegue-k3s-gitops.md) | Despliegue en k3s con flit-gitops y Argo CD | propuesto | A-05 |
| [ATLAS-ADR-0007](0007-persistencia.md) | Persistencia: esquema atlas con prefijos, Redis, S3 y RabbitMQ | propuesto | A-06 |
| [ATLAS-ADR-0008](0008-bff-y-entrada-http-propia.md) | BFF en atlas-api y entrada HTTP propia | propuesto | A-07 |
| [ATLAS-ADR-0009](0009-publicacion-ci-oidc-github.md) | Publicación desde CI con OIDC de GitHub y lista blanca | propuesto | A-08 |
| [ATLAS-ADR-0010](0010-validacion-contratos-oasdiff-vacuum.md) | Validación de contratos con oasdiff y vacuum | propuesto | A-09 |
| [ATLAS-ADR-0011](0011-ia-litellm-y-atlas-ia.md) | IA a través de LiteLLM y orquestador en atlas-ia | propuesto | A-10 |
| [ATLAS-ADR-0012](0012-alcance-mvp.md) | Alcance del MVP: fases F0 a F4 | propuesto | A-11 |
| [ATLAS-ADR-0013](0013-nombre-y-hosts.md) | Nombre «Flit Atlas» y hosts por ambiente | propuesto | A-12 |
| [ATLAS-ADR-0014](0014-mensajeria-outbox.md) | Mensajería: outbox e inbox del SDK de plataforma sobre RabbitMQ | propuesto | F0.2 |
| [ATLAS-ADR-0015](0015-paquetes-compartidos-de-flit.md) | Paquetes compartidos de flit: NuGet del SDK y réplica de tokens de marca | propuesto | F0.2 |
| [ATLAS-ADR-0016](0016-contrato-de-modulo.md) | Contrato de módulo: IModuloAtlas y ModuloAtlas con banderas | propuesto | §10 |

Las fuentes `A-xx` y `§x` remiten a [`docs/atlas/prompts/00-prompt-maestro-flit-atlas.md`](../atlas/prompts/00-prompt-maestro-flit-atlas.md).
