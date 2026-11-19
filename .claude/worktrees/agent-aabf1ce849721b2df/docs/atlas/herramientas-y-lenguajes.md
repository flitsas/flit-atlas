# Herramientas y lenguajes para el portal de documentación técnica

Oct 21, 2026 · @Jorman

## Resumen ejecutivo

El producto es un portal propio que reúne cuatro capacidades: diagramas de arquitectura, diagramas funcionales, manuales dinámicos y contratos de integración con ejemplos curl. La recomendación es un backend en **.NET 10** como núcleo, **Go** para servicios de render y validación, **Python** para IA y procesamiento de documentos, y **React + TypeScript** en el frontend. No se usa Java ni Node como servicio en producción; Node solo compila el frontend.

Tres principios guían cada elección:

- **Escalable:** servicios sin estado detrás de la API central, cola de mensajes para trabajo pesado, caché distribuida y contenedores con escalado horizontal.
- **Seguro:** identidad centralizada (OIDC), autorización por recurso, secretos fuera del código, sanitización de todo contenido que se renderiza y control de dependencias.
- **Dinámico:** todo el contenido se guarda como fuente de texto versionada (C4, Mermaid, BPMN, MDX, OpenAPI) y se renderiza en vivo; los cambios se propagan en tiempo real.

Principio rector: no se programa ningún motor de dibujo ni visor de contratos desde cero. Se integran librerías de código abierto maduras y el valor propio está en el modelo de datos, la integración entre piezas y la automatización.

## Stack base y reparto por lenguaje

Cada lenguaje toma el trabajo donde su ecosistema es más fuerte: .NET la lógica de negocio y la seguridad, Go el procesamiento rápido y sin estado, Python la IA y los documentos, React toda la interacción.

| Capa | Lenguaje | Responsabilidad | Piezas clave |
| --- | --- | --- | --- |
| Frontend | React + TypeScript | Editores, visores, manuales, consola de contratos | Vite, React Router, TanStack Query, React Flow, Mermaid, bpmn-js, Scalar |
| API central y puerta de entrada | .NET 10 (ASP.NET Core) | Autenticación, autorización, catálogo, modelo C4, versiones, contenido, OpenAPI propio | Minimal APIs, EF Core, YARP, Microsoft.AspNetCore.OpenApi, SignalR |
| Servicios de render y validación | Go | Render de D2 a SVG, lint y diff de OpenAPI, trabajos de alto volumen | D2 como librería, vacuum, oasdiff, pact-go |
| Servicios de IA y documentos | Python | Asistente sobre manuales y contratos, exportación PDF/Word, importación de Postman y documentos heredados | FastAPI, pgvector, WeasyPrint, python-docx, pact-python |
| Datos | — | Persistencia, versiones, búsqueda, caché | PostgreSQL (+ pgvector), Redis, almacenamiento de objetos S3 compatible, Git |
| Mensajería | — | Trabajo asíncrono entre servicios | RabbitMQ o NATS |

El frontend habla solo con la API .NET. .NET llama a Go y Python por gRPC (llamadas síncronas) o publica trabajos en la cola (render masivo, exportaciones, indexación).

&#91;embedded content: arquitectura general · 4 capas\]

Solo la API .NET queda expuesta a internet; Go y Python reciben llamadas internas y comparten la capa de datos.

## Caso 1: diagramas de arquitectura de software

Se recomienda un modelo C4 propio guardado en PostgreSQL, editado en un lienzo React y renderizado con D2 desde Go. Un solo modelo genera todas las vistas (contexto, contenedores, componentes), como hace Structurizr, pero sin depender de Java.

| Capa | Herramienta | Lenguaje | Por qué |
| --- | --- | --- | --- |
| Lienzo de edición | React Flow | TypeScript | Lienzo interactivo maduro: nodos, conexiones, zoom, selección múltiple |
| Acomodo automático | ELK.js | TypeScript | Algoritmos de layout jerárquico para grafos grandes |
| Vista rápida en texto | Mermaid (sintaxis C4) | TypeScript | Se renderiza en el navegador sin servidor |
| Modelo C4 | Entidades propias con EF Core | .NET 10 | Sistemas, contenedores, componentes y relaciones como datos consultables y versionados |
| Render de alta calidad | D2 como librería | Go | Mejor acomodo automático para arquitectura; D2 está escrito en Go y se integra sin procesos externos |
| Exportación | SVG y PNG desde el servicio Go | Go | Para incrustar en manuales y para descargar |

**Escalable:** el render en Go es sin estado; cada SVG se guarda en caché por hash del modelo, así un diagrama no se vuelve a renderizar si no cambió.

**Seguro:** el SVG generado se sanitiza antes de servirse (sin scripts ni enlaces externos) y se aplica autorización por sistema: cada equipo ve solo lo que le corresponde.

**Dinámico:** como el modelo es un dato y no un dibujo, un cambio en un sistema se refleja en todas las vistas y en todos los manuales que lo incrustan.

## Caso 2: diagramas funcionales

Se recomiendan dos formatos complementarios: Mermaid para flujos, secuencias y estados escritos como texto, y BPMN con bpmn-js para procesos de negocio formales. Ambos se editan y renderizan en el navegador; el backend solo guarda y versiona la fuente.

| Necesidad | Herramienta | Lenguaje | Por qué |
| --- | --- | --- | --- |
| Flujos, secuencias, estados, ER | Mermaid | TypeScript | El formato de diagramas como código con más soporte; se renderiza en el navegador |
| Procesos de negocio BPMN 2.0 | bpmn-js | TypeScript | Editor BPMN completo con validación del estándar y exportación a XML |
| Editor de texto con vista previa | Monaco Editor | TypeScript | El mismo editor de VS Code, con resaltado y autocompletado |
| Almacenamiento y versiones | API con EF Core | .NET 10 | Guarda la fuente Mermaid o el XML BPMN con historial y autor |
| Validación en servidor | Parser BPMN XML | .NET 10 | Rechaza XML mal formado antes de guardarlo |
| Generación asistida | Servicio de IA | Python | Propone un diagrama a partir de una descripción o de un caso de uso escrito |

**Escalable:** el render ocurre en el navegador de cada usuario, así que el servidor no carga con ese trabajo.

**Seguro:** Mermaid se configura en modo `securityLevel: strict` para bloquear HTML y scripts dentro de los diagramas; el XML BPMN se valida y se limpia en .NET antes de guardarse. Revisar la licencia de bpmn-js, que exige mantener visible la marca bpmn.io.

**Dinámico:** un diagrama funcional puede vincularse a componentes del modelo C4, de modo que cada paso del flujo apunte al sistema que lo ejecuta.

## Caso 3: manuales dinámicos

Se recomienda que los manuales vivan dentro de la misma aplicación React, escritos en MDX y guardados en la base de datos. Así un manual puede incrustar diagramas y contratos vivos, y respetar los mismos permisos del resto del portal. Docusaurus queda como alternativa solo si se necesita un sitio público estático separado.

| Necesidad | Herramienta | Lenguaje | Por qué |
| --- | --- | --- | --- |
| Formato de contenido | MDX | TypeScript | Markdown con componentes React incrustados: diagramas, contratos, alertas, pestañas |
| Editor | Tiptap (o Monaco para modo código) | TypeScript | Edición visual para no técnicos y edición en texto para desarrolladores |
| Contenido, versiones y permisos | API con EF Core | .NET 10 | Versiones por release, estados borrador/publicado, autor y aprobaciones |
| Búsqueda de texto | Full-text de PostgreSQL | .NET 10 | Sin servicio adicional; escala a decenas de miles de páginas |
| Búsqueda semántica y asistente IA | pgvector + FastAPI | Python | Respuestas sobre los manuales y contratos con cita de la página origen |
| Exportación PDF y Word | WeasyPrint y python-docx | Python | Versiones imprimibles o para enviar a clientes |
| Imágenes y adjuntos | Almacenamiento de objetos S3 compatible | .NET 10 | Archivos fuera de la base de datos, servidos por URL firmada |

**Escalable:** las páginas publicadas se sirven desde caché (Redis y CDN); solo los borradores consultan la base de datos. La exportación y la indexación corren en cola, no en la petición del usuario.

**Seguro:** el MDX se compila con una lista blanca de componentes permitidos; nunca se ejecuta código arbitrario escrito por un usuario. Los adjuntos se validan por tipo y tamaño y se escanean antes de publicarse.

**Dinámico:** un componente como `<Contrato id="pagos-v2" />` o `<Diagrama sistema="facturacion" />` muestra siempre la versión vigente, sin copiar y pegar.

## Caso 4: contratos de integración con ejemplos curl

Se recomienda OpenAPI 3.1 (y AsyncAPI para eventos) como única fuente de verdad de cada integración. .NET 10 genera el contrato de las APIs propias desde el código, Go lo valida y detecta cambios incompatibles, y React lo muestra con ejemplos curl generados automáticamente.

| Necesidad | Herramienta | Lenguaje | Por qué |
| --- | --- | --- | --- |
| Generar OpenAPI de APIs propias | Microsoft.AspNetCore.OpenApi | .NET 10 | Paquete oficial; en .NET 10 genera OpenAPI 3.1 por defecto |
| Visor y consola "probar ahora" | Scalar (componente React) | TypeScript | Documentación moderna con cliente de API integrado |
| Ejemplos curl y en otros lenguajes | httpsnippet | TypeScript | Convierte cada operación en curl, C#, Python, Go, JS |
| Lint de reglas de diseño | vacuum | Go | Compatible con reglas Spectral, sin depender de Node |
| Detección de cambios incompatibles | oasdiff | Go | Compara dos versiones y bloquea la publicación si rompe consumidores |
| Pruebas de contrato | PactNet, pact-go, pact-python | .NET, Go, Python | Cada servicio prueba sus contratos en su propio lenguaje |
| Importar contratos existentes | Conversores Postman y documentos | Python | Pasa colecciones Postman y documentación heredada a OpenAPI |
| Catálogo de integraciones | API con EF Core | .NET 10 | Dueño, consumidores, ambientes, versión vigente, estado de pruebas |

**Escalable:** las specs se versionan en Git y se cachean por versión; la validación con Go corre en la cola y en el pipeline de CI, no en la petición del usuario.

**Seguro:** la consola "probar ahora" apunta solo a ambientes de prueba, nunca a producción. Los ejemplos curl usan variables (`$TOKEN`) y jamás muestran credenciales reales. El lint incluye reglas de seguridad: autenticación declarada en cada operación y sin datos sensibles en la URL.

**Dinámico:** cuando .NET publica una nueva versión de su API, el contrato, los ejemplos curl y los manuales que lo incrustan se actualizan solos; oasdiff avisa a los consumidores afectados antes de que algo se rompa.

## Escalabilidad transversal

La clave es que ningún servicio guarde estado en memoria: así cualquiera puede multiplicarse detrás de un balanceador según la carga.

| Área | Herramienta | Lenguaje o integración | Uso |
| --- | --- | --- | --- |
| Contenedores | Docker + Kubernetes | Todos | Despliegue uniforme y escalado horizontal automático (HPA) |
| Base de datos | PostgreSQL con réplicas de lectura | .NET (EF Core), Python (SQLAlchemy) | Escrituras en el primario, lecturas en réplicas |
| Caché distribuida | Redis | .NET (HybridCache), Go, Python | Páginas publicadas, SVG renderizados, sesiones |
| Mensajería | RabbitMQ o NATS | MassTransit (.NET), clientes oficiales en Go y Python | Render masivo, exportaciones, indexación, notificaciones |
| Comunicación interna | gRPC | .NET, Go, Python | Llamadas síncronas tipadas y rápidas entre servicios |
| Archivos | Almacenamiento S3 compatible + CDN | .NET | Adjuntos, imágenes y exportaciones fuera de la base de datos |
| Observabilidad | OpenTelemetry + Grafana (Prometheus, Loki, Tempo) | SDK oficial en los tres lenguajes | Métricas, logs y trazas de una petición a través de todos los servicios |
| Orquestación local | .NET Aspire | .NET | Levanta todos los servicios, incluidos Go y Python, en desarrollo |

Regla práctica: toda operación que tarde más de un segundo (render en lote, exportar PDF, indexar para IA) sale de la petición HTTP y se procesa en la cola.

## Seguridad transversal

Toda la seguridad de acceso se concentra en la API .NET: los servicios Go y Python no se exponen a internet y solo aceptan llamadas internas autenticadas.

| Área | Herramienta | Dónde | Uso |
| --- | --- | --- | --- |
| Identidad | OpenID Connect con Keycloak, Entra ID u otro proveedor | .NET (validación de JWT), React (inicio de sesión con PKCE) | Inicio de sesión único y MFA |
| Autorización | Políticas de ASP.NET Core por recurso | .NET | Permisos por sistema, integración y manual (ver, editar, aprobar) |
| Puerta de entrada | YARP con límite de peticiones | .NET | Un solo punto de entrada, rate limiting nativo, cabeceras de seguridad |
| Tráfico interno | mTLS entre servicios | Kubernetes (malla de servicios o certificados) | Go y Python solo aceptan llamadas de .NET |
| Secretos | HashiCorp Vault o el gestor de secretos de la nube | Todos | Ninguna credencial en código ni en variables visibles |
| Contenido renderizado | DOMPurify, Mermaid en modo estricto, MDX con lista blanca | React | Evita inyección de scripts en diagramas y manuales |
| Cabeceras HTTP | Content-Security-Policy estricta | .NET | Bloquea scripts no autorizados en el navegador |
| Dependencias | Dependabot o Renovate + escaneo de imágenes (Trivy) | CI | Detecta librerías y contenedores vulnerables |
| Análisis de código | CodeQL o SonarQube | CI | Revisa C#, Go, Python y TypeScript |
| Auditoría | Registro de cambios inmutable | .NET + PostgreSQL | Quién cambió qué contrato, diagrama o manual, y cuándo |

Los ejemplos curl y la consola de pruebas nunca deben poder apuntar a producción ni mostrar credenciales reales.

## Dinamismo

El portal es dinámico porque todo es una fuente de texto versionada que se renderiza al vuelo y se enlaza con lo demás, en lugar de imágenes o documentos estáticos.

| Capacidad | Herramienta | Lenguaje | Resultado |
| --- | --- | --- | --- |
| Actualización en tiempo real | SignalR | .NET + cliente React | Quien tiene abierto un manual o diagrama ve los cambios al instante |
| Edición colaborativa simultánea | Yjs (CRDT) | React, con persistencia en .NET | Varias personas editan el mismo manual o diagrama sin conflictos |
| Contenido vivo enlazado | Componentes MDX propios | React | Manuales que incrustan la versión vigente de diagramas y contratos |
| Sincronización con el código | Webhooks de Git + pipeline CI | .NET, Go | Al publicar una API, su contrato y su documentación se actualizan solos |
| Asistente conversacional | RAG sobre manuales y contratos | Python | Responde preguntas citando la página o el contrato fuente |
| Acceso para agentes de IA | Servidor MCP propio | Python o .NET | Herramientas como Claude o Cursor consultan contratos y manuales directamente |

El servidor MCP sigue la tendencia de 2026 de documentar para personas y para agentes de IA a la vez.

## Hoja de ruta de implementación

&#91;embedded content: hoja de ruta · 5 fases\]

Cada fase entrega algo usable y sirve de base a la siguiente. Las fechas dependen del tamaño del equipo, por eso no se fijan aquí.

## Fuentes

- [Top Software Architecture Diagram Tools for 2026 (Geekflare)](https://geekflare.com/software/best-software-architecture-diagram-tools/)
- [Top 9 tools for C4 model diagrams (IcePanel)](https://icepanel.io/blog/top-9-tools-for-c4-model-diagrams)
- [Diagrams as Code in 2026: Mermaid, PlantUML, D2, Excalidraw (DEV)](https://dev.to/levi_liu/diagrams-as-code-in-2026-mermaid-plantuml-d2-excalidraw-when-to-use-what-4mo9)
- [BPMN.js: toolkit para editores BPMN](https://byte-explorer.medium.com/bpmn-js-a-powerful-toolkit-for-building-bpmn-diagram-editors-f94afdbcf085)
- [Best Docusaurus Alternatives in 2026 (Mintlify)](https://mintlify.com/library/best-docusaurus-alternatives)
- [8 best technical documentation software tools in 2026 (GitBook)](https://www.gitbook.com/blog/best-technical-documentation-tools)
- [Scalar vs Redoc vs Swagger UI (2026)](https://www.pkgpulse.com/guides/scalar-vs-redoc-vs-swagger-ui-api-documentation-2026)
- [Top 5 API docs tools (APIs You Won't Hate)](https://apisyouwonthate.com/blog/top-5-best-api-docs-tools/)
- [How is Pact different to Postman? (PactFlow)](https://pactflow.io/blog/how-is-pact-different-than-postman/)
- [OpenAPI en ASP.NET Core: Microsoft.AspNetCore.OpenApi y OpenAPI 3.1 en .NET 10](https://skills.sh/novotnyllc/dotnet-artisan/dotnet-openapi)
- [Minimal APIs en .NET 10 con Scalar](https://codewithmukesh.com/blog/minimal-apis-aspnet-core)
- [Scalar.AspNetCore (NuGet)](https://www-1.nuget.org/packages/Scalar.AspNetCore)

Los nombres de librerías sin enlace (React Flow, ELK.js, vacuum, oasdiff, httpsnippet, Yjs, MassTransit, YARP) provienen de conocimiento general; conviene verificar versión y licencia de cada una antes de adoptarla.
