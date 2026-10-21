# F0.5 · Preguntas abiertas para el líder técnico

> **Fecha:** 21/10/2026 · **Fuentes:** plan 00 §18; plan 01 §16; prompt maestro §4, §5 y §8; F0.3 y F0.4 de esta carpeta.
> Cada pregunta trae una recomendación y la fase que bloquea. Responde en el PR o en la pregunta misma: al cerrarla se anota la respuesta, la fecha y el ADR que la registra.

## 0. La que abre F1

| ID | Pregunta | Recomendación | Bloquea |
|---|---|---|---|
| **N-00** | ¿Apruebas ATLAS-ADR-0001 a 0016? | Aprobarlos como están y abrir ADR nuevos para lo que cambie según estas respuestas, en vez de reabrirlos. Los que dependen de una respuesta (0006 por N-02, 0013 por N-01, 0014 por N-04) pueden aceptarse con la respuesta incorporada en el mismo PR | Todo F1 |

## 1. Pendientes del plan 00 §18

| ID | Pregunta | Estado | Recomendación | Bloquea |
|---|---|---|---|---|
| **P-01** | ¿`flitsas/flit` pasa a privado? | Abierta. Sigue `PUBLIC` (verificado el 21/10/2026) | **Sí.** Mientras sea público, cualquier contrato que viva allí es público, aunque Atlas lo restrinja. Ojo: si se cierra, el script de réplica de marca de ATLAS-ADR-0015 necesita un token de lectura | F4 |
| **P-02** | ¿Cuándo se retiran los 27 legados? | Abierta | Si es antes de 12 meses, en F3 solo se publican los `BackApiExternal*` y el resto queda como ficha de catálogo | F3 |
| **P-03** | ¿Quién es el responsable funcional de cada aplicativo? | Abierta | Una persona por aplicativo, con un SLA de revisión de borradores (p. ej. 5 días hábiles). Puede ser la misma que el principal técnico al inicio | F5, F7 |
| **P-04** | ¿Proveedores de IA aprobados? | Abierta | Anthropic como principal y OpenAI de respaldo. **Kimi y DeepSeek suspendidos** hasta evaluar la transferencia internacional (Ley 1581, art. 26) | F-IA |
| **P-05** | ¿Nombre y dominio? | Nombre cerrado por A-12 («Flit Atlas»). El dominio .com pasa a N-01 | — | — |
| **P-06** | ¿Los externos se crean en core-identity? | **Obsoleta** por A-03: Atlas tiene identidad propia | Cerrarla y referenciar ATLAS-ADR-0004 | — |
| **P-07** | ¿El sandbox nivel 2 se ofrece a todos o por solicitud? | Abierta | Por solicitud, aprobado por el responsable del aplicativo y con vencimiento | F4 |
| **P-08** | ¿ADR que reemplaza parcialmente al ADR-0070 de `flit` (que descartó un gateway tipo LiteLLM)? | Abierta. Es un ADR **del repo `flit`**, no de Atlas | Escribirlo en `flit`: el router de core-api se conserva y LiteLLM entra como un proveedor más. ATLAS-ADR-0011 lo cita como dependencia. Requiere tu visto bueno para tocar `flit` | F-IA |

## 2. Preguntas nuevas

### Infraestructura y despliegue

| ID | Pregunta | Recomendación | Bloquea |
|---|---|---|---|
| **N-01** | ¿El host de pdn es `atlas.flitsas.com`? | Sí, coherente con `app.flitsas.com` y `api.flitsas.com` | F4 (DNS) |
| **N-02** | ¿Namespaces propios `atlas-{dev,qa,pdn}` o dentro de `flit2-*`? | **Propios** (F0.3 §4): cuota y `NetworkPolicy` aisladas, sobre todo en la 169 | HU 1.8 |
| **N-03** | ¿Confirmas los puertos `x031`–`x035` y gRPC `8087`–`8089`? ¿Y registro de paso `4027`/`8085` de core-notificaciones, que usa el clúster pero no está en `docs/despliegue-y-puertos.md`? | Sí a ambos. Es un PR pequeño en `flit` que requiere tu visto bueno | HU 1.1 (local), HU 1.8 |
| **N-08** | ¿Redis, MinIO y RabbitMQ propios de Atlas o compartidos? | **RabbitMQ respondido el 21/10/2026: propio por ambiente**, con las convenciones de `flit/deploy/rabbitmq` (ATLAS-ADR-0018). Redis y S3: sigue la recomendación de propios en `atlas-*`; el S3 de dev y qa depende de N-18 | HU 1.8 |
| **N-09** | ¿`flit-atlas` usa los runners propios de la 177? | Sí, por costo. Mientras tanto, `ubuntu-latest` | HU 1.1 |
| **N-17** | La 169 está a ~190 ms de Colombia y con CPU escasa. ¿Atlas pdn espera la decisión de Cloudflare o de una VPS en América? | No esperar: diseñar la PWA para tolerar latencia (shell precargado, caché offline) y medir en qa con 190 ms simulados. Si se elige Cloudflare, evaluar antes el tratamiento de datos personales (Ley 1581) | F4 |
| **N-18** | MinIO dejó de publicar imágenes públicas (Docker Hub responde «repository does not exist» y quay.io pide autenticación; verificado el 21/10/2026). ¿Qué S3 usan Atlas dev y qa? | Para **local**, RustFS 1.0.1 (Apache-2.0, compatible con la API de MinIO), ya en el AppHost. Para **dev y qa**: (a) reutilizar la imagen de MinIO que ya corre en la 177, con el riesgo de quedarse sin parches; (b) RustFS, joven pero activo; o (c) SeaweedFS, más maduro pero más complejo de configurar. Recomiendo **(b)**, evaluado en dev durante la HU 1.8. pdn no cambia (Contabo) | HU 1.8 |
| **N-19** | La auditoría es de solo inserción (HU 1.2), pero con el script de flit `flit_atlas` es también **dueño** del esquema, y un dueño puede devolverse el permiso o desactivar el trigger con DDL. ¿Separamos el usuario que migra del que usa la app? | **Sí, antes de F4** (abrir a externos): `flit_atlas_migrador`, dueño del esquema y solo para el migrador, y `flit_atlas` con DML y sin `UPDATE`/`DELETE` sobre la auditoría. Requiere un script propio de Atlas o una opción nueva en el de flit, y un ADR. Hasta entonces la protección vale contra errores y contra un `UPDATE`/`DELETE` corriente, no contra quien controle la credencial | F4 |

### Identidad y Microsoft 365

| ID | Pregunta | Recomendación | Bloquea |
|---|---|---|---|
| **N-04** | ¿Se publica `Flit.Platform.Sdk` como paquete NuGet desde `flit`? | **Respondida el 21/10/2026: no, por ahora.** Atlas usa outbox e inbox propios con el mismo sobre del SDK (ATLAS-ADR-0018) | — |
| **N-05** | ¿Cómo envía correo Atlas (invitaciones, recordatorios)? | **Graph `sendMail` desde el buzón «Invitaciones Atlas»**, restringido con RBAC para aplicaciones de Exchange. SMTP solo si no se aprueba el permiso. core-notificaciones no, porque acopla Atlas al broker de FLIT 2.0 | HU 1.5 |
| **N-07** | ¿Se habilita que un externo con Microsoft 365 entre con la cuenta de **su** tenant (Entra multi-tenant)? | **Después del MVP.** Agrega superficie de ataque (validar tenant y correo de la invitación) en el módulo más sensible. En F1 los externos usan passkey o contraseña con TOTP. Dejar la bandera diseñada pero apagada | — |
| **N-15** | ¿Cómo publica Atlas avisos en un canal de Teams? | Un flujo de **Workflows** (Power Automate) con URL propia por canal, guardada como secreto. Un bot solo si después se necesita interacción | F2 |
| **N-16** | ¿Quién tiene el rol de administrador global de Microsoft 365 para registrar las aplicaciones, dar los consentimientos y crear los buzones? | — | HU 1.3 |

### Arquitectura

| ID | Pregunta | Recomendación | Bloquea |
|---|---|---|---|
| **N-06** | **Contradicción en el prompt maestro:** el diagrama §5.1 conecta los clientes MCP directo a `atlas-ia`, pero las reglas del mismo §5.1 y A-07 dicen que solo `atlas-api` se expone. ¿Cuál vale? | **Solo `atlas-api` se expone.** El MCP entra por una ruta `/mcp` de `atlas-api` que valida el token de OpenIddict (scope `atlas.mcp`) y reenvía por YARP a `atlas-ia` con el contexto de permisos firmado (A-10). Se registra en un ADR al llegar a F7 y se corrige el diagrama | F7 (y el diseño de HU 1.3c) |
| **N-13** | ¿Dónde viven los OpenAPI enriquecidos del legado ICT: en cada `BackApiExternal*` o en un repo de contratos legados? | **Un repo privado de contratos legados.** Los legados se retiran (P-02), el enriquecimiento viene del Excel y no del código, y evita tocar 11 repos. Cada `BackApiExternal*` solo corrige su `tsoa.json` y su CI publica el `swagger.json` crudo como insumo | F3 |
| — | Plan 01 D-3 (código de producto `atlas` en el contrato de plataforma de `flit`) | **Obsoleta** por A-03: Atlas no usa core-identity para el login | — |

### Datos del dominio (para F2)

| ID | Pregunta | Recomendación | Bloquea |
|---|---|---|---|
| **N-10** | ¿Cuál es el horario por defecto del turno de soporte del sábado? | El líder lo define; Atlas no lo inventa (§8.3) | F2 |
| **N-11** | ¿Qué son Sentinel y People? Una línea de descripción para cada uno | — | F2 (semilla del catálogo) |
| **N-12** | Abraham Cañon aparece en turnos hasta agosto y no está en el reparto v5. ¿Se crea inactivo y fuera de la rotación futura? | Sí, como indica el prompt maestro §8.3 | F2 |
