# Informe del enriquecimiento del contrato de ICT de FLIT 2

Generado por `python -m atlas_docs.ict` (HU 3.8, ATLAS-ADR-0037) desde `contracts/openapi/core-ict.v1.yaml` del repo `flit` y el Excel `Contratos_Integraciones_31Jul2025.xlsx`. No se edita a mano: se regenera junto con el spec. No trae valores sensibles: los ejemplos y los hosts internos se describen sin su valor.

## Resumen

| Contrato | Versión base | Versión enriquecida | Visibilidad | Operaciones públicas | Operaciones internas | Webhooks | Agregadas desde el código |
|---|---|---|---|---|---|---|---|
| `core-ict` | 1.0.0 | 1.1.0 | compartible | 10 | 2 | 1 | 5 |

Hosts mezclados y URL vacías del Excel: **resuelto**. core-ict recibe todo por una sola URL base por ambiente (el host público que el Gateway YARP de FLIT 2 enruta a core-ict); el Excel ya no define servidores. Ver [URL del Excel](#url-del-excel-resuelto-con-la-url-base-única).

## Inconsistencias por tipo

| Tipo | Cantidad |
|---|---|
| [URL del Excel (resuelto con la URL base única)](#url-del-excel-resuelto-con-la-url-base-única) | 6 |
| [Servidores por ambiente](#servidores-por-ambiente) | 4 |
| [Servicios del índice sin hoja](#servicios-del-índice-sin-hoja) | 2 |
| [Salidas incompletas](#salidas-incompletas) | 5 |
| [Hojas sin equivalente en core-ict](#hojas-sin-equivalente-en-core-ict) | 3 |
| [Operaciones que core-ict implementa y su contrato no declara](#operaciones-que-core-ict-implementa-y-su-contrato-no-declara) | 5 |
| [Operaciones de core-ict sin hoja en el Excel](#operaciones-de-core-ict-sin-hoja-en-el-excel) | 2 |
| [Correcciones al contrato verificadas en el código de core-ict](#correcciones-al-contrato-verificadas-en-el-código-de-core-ict) | 4 |
| [Nombres con errores](#nombres-con-errores) | 6 |
| [Campos del Excel que core-ict acepta y no usa](#campos-del-excel-que-core-ict-acepta-y-no-usa) | 1 |
| [Esquemas del contrato que no describen la API real](#esquemas-del-contrato-que-no-describen-la-api-real) | 1 |
| [Tipos distintos entre el Excel y el contrato](#tipos-distintos-entre-el-excel-y-el-contrato) | 7 |
| [Obligatoriedad distinta entre el Excel y el contrato](#obligatoriedad-distinta-entre-el-excel-y-el-contrato) | 5 |
| [Ejemplo y tabla de campos no coinciden](#ejemplo-y-tabla-de-campos-no-coinciden) | 4 |
| [Ejemplos reparados o ajustados al esquema](#ejemplos-reparados-o-ajustados-al-esquema) | 12 |
| [Seguridad declarada distinta](#seguridad-declarada-distinta) | 3 |
| [Tablas de referencia con datos dudosos u omitidas](#tablas-de-referencia-con-datos-dudosos-u-omitidas) | 6 |
| [Códigos de respuesta](#códigos-de-respuesta) | 6 |
| [Campos que exige el contrato y el Excel no documenta](#campos-que-exige-el-contrato-y-el-excel-no-documenta) | 1 |
| [Metadatos del contrato y del Excel](#metadatos-del-contrato-y-del-excel) | 10 |
| [Textos del contrato reescritos en lenguaje funcional](#textos-del-contrato-reescritos-en-lenguaje-funcional) | 5 |
| **Total** | **98** |

## Pendientes para el líder técnico

- [por verificar] Confirmar que `ict.qa.flitsas.com` (ruta `ict-host-route` del Gateway) responde en QA: el ingress de QA no está en `flit-gitops`. El Excel usaba otro host de QA.
- [por verificar] Decidir si RequestSign y StatusSign existen (están en el índice y no tienen hoja).
- [por verificar] Decidir si SearchPlate, StatusPlate y WebHookPlate se implementan en core-ict: no tienen equivalente.
- [por verificar] Agregar al contrato de `flit` las operaciones que core-ict implementa y no declara, con la propuesta de `propuesta-flit/` (PR en la ola 3, HU 3.10).
- [por verificar] Confirmar si core-ict debe autenticarse ante el gestor en el webhook (hoy no envía `Authorization`).
- [por verificar] Revisar los campos que el contrato exige y el Excel no documenta, y los tipos que no coinciden.

## URL del Excel (resuelto con la URL base única)

| Hoja | Detalle |
|---|---|
| — | `*.execute-api.us-east-1.amazonaws.com (API Gateway de AWS; id omitido)` en «3 - Attachments»: resuelto, el spec usa `ict.flitsas.com` (pdn), `ict.qa.flitsas.com` (qa) |
| — | `ict.qa.flitsas.online` en «1 - Auth», «10 - PauseDraftProcess», «11 - AbortProcess», «2 - Transacction», «3 - Attachments», «5 - StatusTransaction», «9 - Secretaries»: resuelto, el spec usa `ict.flitsas.com` (pdn), `ict.qa.flitsas.com` (qa) |
| — | `servidor local (omitido)` en «5 - StatusTransaction»: resuelto, el spec usa `ict.flitsas.com` (pdn), `ict.qa.flitsas.com` (qa) |
| — | `status.transact.api.qa.flitsas.online` en «5 - StatusTransaction»: resuelto, el spec usa `ict.flitsas.com` (pdn), `ict.qa.flitsas.com` (qa) |
| — | `transact.api.qa.flitsas.online` en «10 - PauseDraftProcess», «11 - AbortProcess», «9 - Secretaries»: resuelto, el spec usa `ict.flitsas.com` (pdn), `ict.qa.flitsas.com` (qa) |
| — | 11 hojas no traen URL de dev, qa o pdn: resuelto, los servidores salen del Gateway de FLIT 2 (`services/core-api/src/Flit.Gateway/appsettings.json`) |

## Servidores por ambiente

| Hoja | Detalle |
|---|---|
| — | el contrato declara el servidor `api.flit.co` (Producción (vía Gateway YARP)), que no es una ruta del Gateway hacia core-ict; se reemplaza por la URL base única del ambiente |
| — | el contrato declara el servidor `servidor local (omitido)` (Local (Gateway)), que no es una ruta del Gateway hacia core-ict; se reemplaza por la URL base única del ambiente |
| — | `ict.dev.flitsas.com` del Gateway no se publica: el ambiente dev es de uso interno de FLIT y no se publica |
| — | `host local o interno (omitido)` del Gateway no se publica: no es un host público de FLIT |

## Servicios del índice sin hoja

| Hoja | Detalle |
|---|---|
| — | «RequestSign» está en el índice («Servicio de solicitud de validacion de identidad») y no tiene hoja |
| — | «StatusSign» está en el índice («Servicio de consulta de estado de una validacion de identidad») y no tiene hoja |

## Salidas incompletas

| Hoja | Detalle |
|---|---|
| 4 - WebHookTransaction | core-ict solo mira el código HTTP de la respuesta del gestor (2xx = entregado); el cuerpo que documenta el Excel en «Salida» no se lee |
| 10 - PauseDraftProcess | `pauseProcess` está bajo «Salida» y es un campo de la petición; la respuesta real (`putExternalTransactionPauseDraftProcessById`) no está documentada en el Excel |
| 10 - PauseDraftProcess | `observation` está bajo «Salida» y es un campo de la petición; la respuesta real (`putExternalTransactionPauseDraftProcessById`) no está documentada en el Excel |
| 11 - AbortProcess | `observation` está bajo «Salida» y es un campo de la petición; la respuesta real (`putExternalTransactionAbortProcessById`) no está documentada en el Excel |
| 6 - SearchPlate | la salida solo confirma el registro de la placa; el estado y los gastos no tienen salida documentada en esta hoja (§8.4, punto 8) |

## Hojas sin equivalente en core-ict

| Hoja | Detalle |
|---|---|
| 6 - SearchPlate | core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 7 - WebHookPlate | core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente |
| 8 - StatusPlate | core-ict no implementa el estado de placas (ni en su contrato ni en su código) |

## Operaciones que core-ict implementa y su contrato no declara

| Hoja | Detalle |
|---|---|
| — | `POST /api/v1/transact-attachments/upload` está en `services/core-ict/src/Flit.Ict.Api/Endpoints/IctAttachmentEndpoints.cs (MapV1TransactAttachments)` y no en el contrato; se agrega con el método, la seguridad, el cuerpo y los códigos del código |
| — | `GET /api/v1/secretaries` está en `services/core-ict/src/Flit.Ict.Api/Endpoints/IctSecretariesEndpoints.cs` y no en el contrato; se agrega con el método, la seguridad, el cuerpo y los códigos del código |
| — | `PUT /api/v1/external-transaction/pauseDraftProcess/{id}` está en `services/core-ict/src/Flit.Ict.Api/Endpoints/IctLifecycleEndpoints.cs (PauseAsync)` y no en el contrato; se agrega con el método, la seguridad, el cuerpo y los códigos del código |
| — | `PUT /api/v1/external-transaction/abortProcess/{id}` está en `services/core-ict/src/Flit.Ict.Api/Endpoints/IctLifecycleEndpoints.cs (AbortAsync)` y no en el contrato; se agrega con el método, la seguridad, el cuerpo y los códigos del código |
| 4 - WebHookTransaction | el webhook `estadoTramite` sale de `Flit.Ict.Infrastructure/Jobs/WebhookNotificationJob.cs (TryDeliverAsync)`; el contrato no declara webhooks |

## Operaciones de core-ict sin hoja en el Excel

| Hoja | Detalle |
|---|---|
| — | 3 operaciones del cliente ICT (`ictClientToken`) no tienen hoja en el Excel; se publican con lo que trae el contrato: `GET /api/v1/ict/status/{id}`, `POST /api/v1/ict/reprocess/{id}`, `PATCH /api/v1/pretramites/{id}` |
| — | 2 operaciones usan el JWT de plataforma y no son para clientes; quedan `x-internal` (la poda de Atlas las quita): `GET /api/v1/ict/logs`, `GET /api/v1/ict/alerts` |

## Correcciones al contrato verificadas en el código de core-ict

| Hoja | Detalle |
|---|---|
| — | `StatusProcessV1Item` (`Flit.Ict.Domain/Abstractions/IStatusProcessV1Query.cs`) define los ítems del histórico (camelCase al serializar); el contrato los dejaba como objeto sin propiedades |
| — | `IctAuthEndpoints.LoginAsync` devuelve `{ token, expiresInSeconds }` (o `{ mustRotate: true }`); el contrato declaraba `expiresAt` |
| — | `IctAuthEndpoints.LoginAsync` responde 403 a un cliente inactivo o bloqueado; el contrato solo declaraba 401 |
| — | `IctRegisterEndpoints.RegisterAsync` responde 400 si el cuerpo no se puede leer; el contrato no lo declaraba |

## Nombres con errores

| Hoja | Detalle |
|---|---|
| — | `Firts` en ejemplos de «2 - Transacction»: «Firts» por «First». El spec conserva el nombre que usa la API |
| — | `Representate` en descripciones de «2 - Transacction»: «Representate» por «Representante». El spec conserva el nombre que usa la API |
| — | `StatusProccess` en campos de «5 - StatusTransaction», campos de «8 - StatusPlate», ejemplos de «5 - StatusTransaction», ejemplos de «8 - StatusPlate»: «Proccess» por «Process». El spec conserva el nombre que usa la API |
| — | `Transacction` en nombre de hoja, índice del Excel: «Transacction» por «Transaction». El spec conserva el nombre que usa la API |
| — | `crecion` en descripciones de «8 - StatusPlate»: «crecion» por «creación». El spec conserva el nombre que usa la API |
| — | `trasaccion` en descripciones de «2 - Transacction»: «trasaccion» por «transacción». El spec conserva el nombre que usa la API |

## Campos del Excel que core-ict acepta y no usa

| Hoja | Detalle |
|---|---|
| 2 - Transacction | `company_manager_id`: core-ict deriva la compañía del token y no lee `company_manager_id` (`RegisterRowInput` no lo declara); se documenta como obsoleto |

## Esquemas del contrato que no describen la API real

| Hoja | Detalle |
|---|---|
| 2 - Transacction | `postExternalTransactionRegister`: el contrato declara un objeto `{ transactions: [...] }` con filas sin propiedades; core-ict (`IctRegisterEndpoints.RegisterAsync`) lee un arreglo JSON de filas `RegisterRowInput` en snake_case con `seller`, `buyer` y `lessee`, así que el esquema de la petición sale del Excel |

## Tipos distintos entre el Excel y el contrato

| Hoja | Detalle |
|---|---|
| 2 - Transacction | `Detail/TransactionFlit`: Excel «Integer», contrato `string` (se conserva el del contrato) |
| 3 - Attachments | `transactionFlit`: Excel «number», contrato `string` (se conserva el del contrato) |
| 3 - Attachments | `file`: el Excel dice base64 y el servicio recibe el archivo binario (multipart) |
| 4 - WebHookTransaction | `TransactionFlit`: Excel «number», contrato `string` (se conserva el del contrato) |
| 5 - StatusTransaction | parámetro `id` de `getStatusProcessById`: Excel «number», contrato `string` (se conserva el del contrato) |
| 10 - PauseDraftProcess | parámetro `id` de `putExternalTransactionPauseDraftProcessById`: Excel «number», contrato `string` (se conserva el del contrato) |
| 11 - AbortProcess | parámetro `id` de `putExternalTransactionAbortProcessById`: Excel «number», contrato `string` (se conserva el del contrato) |

## Obligatoriedad distinta entre el Excel y el contrato

| Hoja | Detalle |
|---|---|
| 1 - Auth | `Token`: obligatorio en el Excel y opcional en el contrato |
| 2 - Transacction | 47 reglas de obligatoriedad condicionales («si el vendedor es un NIT», «si usan mandante»…) quedan como texto en `x-flit-obligatoriedad`; no se modelan como esquema |
| 3 - Attachments | `closed`: obligatorio en el Excel y opcional en el contrato |
| 4 - WebHookTransaction | `ManagerIdTransaction`: opcional en el Excel y obligatorio en el contrato (manda el contrato) |
| 4 - WebHookTransaction | `status`: opcional en el Excel y obligatorio en el contrato (manda el contrato) |

## Ejemplo y tabla de campos no coinciden

| Hoja | Detalle |
|---|---|
| 2 - Transacction | `traffic_secretary_code`: el ejemplo trae integer y la tabla dice «String(10)» |
| 2 - Transacction | campos de la tabla («parametros») que el ejemplo no trae: `armor_level_number_id`, `new_vehicle_fuel_type`, `UrlWebHook` |
| 5 - StatusTransaction | campos de la tabla («salida») que el ejemplo no trae: `StatusProccess/Status`, `StatusProccess/Statusdescription`, `StatusProccess/observation`, `StatusProccess/usermailregistered`, `StatusProccess/userroleregistered`, `StatusProccess/usernameregistered`, `StatusProccess/dateregistered`, `StatusProccess/companyregistered` |
| 5 - StatusTransaction | claves del ejemplo («salida») que la tabla no documenta: `statusprocess` |

## Ejemplos reparados o ajustados al esquema

| Hoja | Detalle |
|---|---|
| 1 - Auth | ejemplo reparado: clave con espacios sobrantes: «password» |
| 1 - Auth | ejemplo reparado: comillas tipográficas cambiadas por comillas rectas |
| 1 - Auth | ejemplo reparado: 3 documentos JSON seguidos en la misma celda; comillas tipográficas cambiadas por comillas rectas |
| 2 - Transacction | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |
| 2 - Transacction | postExternalTransactionRegister (petición): $[0].traffic_secretary_code: número o lógico donde se espera texto |
| 2 - Transacction | postExternalTransactionRegister (respuesta): $.Detail[0].TransactionFlit: número o lógico donde se espera texto; $.Detail[1].TransactionFlit: nulo en un campo que no admite nulos (se usa un valor ficticio) |
| 3 - Attachments | ejemplo reparado: 5 documentos JSON seguidos en la misma celda |
| 3 - Attachments | postTransactAttachmentsUpload (petición): $.transactionFlit: número o lógico donde se espera texto |
| 4 - WebHookTransaction | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |
| 4 - WebHookTransaction | notificarEstadoTramite (petición): $.transactionFlit: número o lógico donde se espera texto; $.ictEstado: obligatorio que faltaba (se agrega con un valor ficticio) |
| 5 - StatusTransaction | getStatusProcessById (respuesta): $.StatusValidation: clave con otra capitalización (se usa la del contrato); $.MessageValidation: clave con otra capitalización (se usa la del contrato); $.statusProcess[0].Status: clave con otra capitalización (se usa la del contrato); $.statusProcess[0].status: texto donde se espera número; $.statusProcess[0].Statusdescription: clave con otra capitalización (se usa la del contrato); $.statusProcess[0].usermailregistered: clave con otra capitalización (se usa la del contrato); $.statusProcess[0].userroleregistered: clave con otra capitalización (se usa la del contrato); $.statusProcess[0].usernameregistered: clave con otra capitalización (se usa la del contrato)… |
| 9 - Secretaries | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |

## Seguridad declarada distinta

| Hoja | Detalle |
|---|---|
| 4 - WebHookTransaction | core-ict no envía cabecera `Authorization` en la notificación (el Excel dice `Bearer`); el gestor no puede autenticar a FLIT por token. Confirmar si se agrega una firma o un token |
| — | [por verificar] `ictClientToken`: cumplimiento de RFC8725 en el emisor del JWT |
| — | [por verificar] `platformJwt`: cumplimiento de RFC8725 en el emisor del JWT |

## Tablas de referencia con datos dudosos u omitidas

| Hoja | Detalle |
|---|---|
| 2 - Transacction | «Tabla Tipo Operacion Garantia»: la fila 1 trae en el ID un valor que no es número (un enlace interno); se toma 1 por la secuencia de la tabla. Confirmar |
| 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 14 no tiene nombre en el Excel (se deja como «Sin nombre definido»; confirmar) |
| 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 15 no tiene nombre en el Excel (se deja como «Sin nombre definido»; confirmar) |
| 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 16 no tiene nombre en el Excel (se deja como «Sin nombre definido»; confirmar) |
| 3 - Attachments | «Tabla para guardar los ids de los archivos adjuntos» (I27) no pasa al spec: diseño interno de persistencia; no es parte del contrato |
| 6 - SearchPlate | «Tabla Tipo Documento» (I6) queda fuera: la hoja no tiene equivalente en core-ict |

## Códigos de respuesta

| Hoja | Detalle |
|---|---|
| 1 - Auth | `postAuthLogin`: el contrato declara el error 401 sin cuerpo; el ejemplo del Excel (forma v1 `{ message }`) no se usa |
| 1 - Auth | `postAuthLogin`: el Excel muestra un error 404 que core-ict no declara; no se agrega su cuerpo v1 |
| 1 - Auth | `postAuthLogin`: el Excel muestra un error 500 que core-ict no declara; no se agrega su cuerpo v1 |
| 3 - Attachments | `postTransactAttachmentsUpload`: el ejemplo de error 400 del Excel no cumple el esquema del contrato ($.message: clave con otra capitalización (se usa la del contrato); $.Status: obligatorio que faltaba (se agrega con un valor ficticio)); no se usa |
| 3 - Attachments | `postTransactAttachmentsUpload`: el ejemplo de error 401 del Excel no cumple el esquema del contrato ($.message: clave con otra capitalización (se usa la del contrato); $.Status: obligatorio que faltaba (se agrega con un valor ficticio)); no se usa |
| — | códigos de la tabla del Excel que ninguna operación de core-ict declara: 204 |

## Campos que exige el contrato y el Excel no documenta

| Hoja | Detalle |
|---|---|
| 4 - WebHookTransaction | `notificarEstadoTramite` exige `ictEstado` y el Excel no lo documenta |

## Metadatos del contrato y del Excel

| Hoja | Detalle |
|---|---|
| — | «WebHookTransaction» no tiene descripción en el índice |
| — | «WebHookPlate» no tiene descripción en el índice |
| — | la hoja «Hoja2» está vacía |
| 4 - WebHookTransaction | sin versión, fecha ni colaborador |
| 7 - WebHookPlate | sin versión, fecha ni colaborador |
| 6 - SearchPlate | sin versión, fecha ni colaborador |
| 8 - StatusPlate | sin versión, fecha ni colaborador |
| — | los parámetros compartidos del contrato (`ManagerIdTransaction`) se copian en cada operación para que el Excel describa cada uso |
| — | el contrato no declara `operationId`; se generan desde el método y la ruta: `POST /api/v1/auth/login` → `postAuthLogin`, `POST /api/v1/external-transaction/register` → `postExternalTransactionRegister`, `GET /api/v1/status-process/byId/{id}` → `getStatusProcessById`, `GET /api/v1/ict/status/{id}` → `getIctStatusById`, `POST /api/v1/ict/reprocess/{id}` → `postIctReprocessById`, `PATCH /api/v1/pretramites/{id}` → `patchPretramitesById`, `GET /api/v1/ict/logs` → `getIctLogs`, `GET /api/v1/ict/alerts` → `getIctAlerts`, `POST /api/v1/transact-attachments/upload` → `postTransactAttachmentsUpload`, `GET /api/v1/secretaries` → `getSecretaries`, `PUT /api/v1/external-transaction/pauseDraftProcess/{id}` → `putExternalTransactionPauseDraftProcessById`, `PUT /api/v1/external-transaction/abortProcess/{id}` → `putExternalTransactionAbortProcessById` |
| — | 1 esquemas del contrato quedaron sin uso tras el enriquecimiento y se quitaron: `RegisterBatchRequest` |

## Textos del contrato reescritos en lenguaje funcional

| Hoja | Detalle |
|---|---|
| — | `/paths/~1api~1v1~1external-transaction~1register/post/description`: nombra el pipeline interno y el transporte entre servicios (gRPC); el original queda en `x-flit-descripcion-contrato` |
| — | `/paths/~1api~1v1~1ict~1status~1{id}/get/description`: nombra una pieza de la arquitectura interna («Plano C»); el original queda en `x-flit-descripcion-contrato` |
| — | `/paths/~1api~1v1~1ict~1reprocess~1{id}/post/description`: describe la cola interna de validación («reencola el pipeline», «flags»); el original queda en `x-flit-descripcion-contrato` |
| — | `/paths/~1api~1v1~1pretramites~1{id}/patch/description`: nombra un número de bug y la cola interna de validación; el original queda en `x-flit-descripcion-contrato` |
| — | `info.description` del contrato nombra el servicio, la feature y el Gateway; se reemplaza por una descripción funcional y el original queda en `info.x-flit-fuente.descripcion_contrato` |

## Campos del Excel fuera del spec

De 178 campos del Excel, 138 quedaron en el spec y 40 solo en este informe. `trazabilidad.json` dice dónde quedó cada uno (archivo y puntero JSON).

| Hoja | Celda | Sección | Campo | Motivo |
|---|---|---|---|---|
| 9 - Secretaries | A12 | parametros | `N/A` | la hoja indica que la operación no tiene parámetros |
| 6 - SearchPlate | A12 | parametros | `CompanyManagerId` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A13 | parametros | `CompanyManagerDocument` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A14 | parametros | `ManagerUser` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A15 | parametros | `ManagerMail` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A16 | parametros | `ManagerIdTransaction` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A17 | parametros | `OwnerDocumentType` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A18 | parametros | `OwnerDocumentNumber` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A19 | parametros | `Plate` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A20 | parametros | `UrlWebHook` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A26 | salida | `TotalRows` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A27 | salida | `TotalRowsProcessed` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A28 | salida | `Detail/Plate` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A29 | salida | `Detail/Status` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 6 - SearchPlate | A30 | salida | `Detail/Message` | sin equivalente en core-ict: core-ict no implementa la consulta de placas (ni en su contrato ni en su código) |
| 7 - WebHookPlate | A12 | parametros | `Plate` | sin equivalente en core-ict: core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente |
| 7 - WebHookPlate | A13 | parametros | `ManagerIdTransaction` | sin equivalente en core-ict: core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente |
| 7 - WebHookPlate | A19 | salida | `Status` | sin equivalente en core-ict: core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente |
| 7 - WebHookPlate | A20 | salida | `Message` | sin equivalente en core-ict: core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente |
| 8 - StatusPlate | A12 | parametros | `CompanyManagerId` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A13 | parametros | `CompanyManagerDocument` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A14 | parametros | `Plate` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A20 | salida | `StatusValidation` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A21 | salida | `MessageValidation` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A22 | salida | `StatusProccess/Status` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A23 | salida | `StatusProccess/Statusdescription` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A24 | salida | `StatusProccess/Observation` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A25 | salida | `StatusProccess/Plate` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A26 | salida | `StatusProccess/Settlement/` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A27 | salida | `StatusProccess/Settlement/Type` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A28 | salida | `StatusProccess/Settlement/DescriptionType` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A29 | salida | `StatusProccess/Settlement/DescriptionGeneral` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A30 | salida | `StatusProccess/Settlement/OrderNumber` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A31 | salida | `StatusProccess/Settlement/PaymentMethod` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A32 | salida | `StatusProccess/Settlement/Amount` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A33 | salida | `StatusProccess/Settlement/CreationDate` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A34 | salida | `StatusProccess/Settlement/ApprovalDate` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A35 | salida | `StatusProccess/Settlement/Bank` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A36 | salida | `StatusProccess/Settlement/CUS` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |
| 8 - StatusPlate | A37 | salida | `StatusProccess/Settlement/Franchise` | sin equivalente en core-ict: core-ict no implementa el estado de placas (ni en su contrato ni en su código) |

## Changelog inicial

Sale de la versión, la fecha y el colaborador de cada hoja. El colaborador queda genérico: el spec se comparte con clientes y el nombre de una persona es un dato personal (Ley 1581, ATLAS-ADR-0037). La última fila es la versión enriquecida del contrato.

| Versión | Fecha | Hoja | Colaborador |
|---|---|---|---|
| 1.5.0 | 16/05/2025 | 1 - Auth | Equipo de integraciones de FLIT |
| 1.8.0 | 16/05/2025 | 3 - Attachments | Equipo de integraciones de FLIT |
| 1.7.0 | 16/05/2025 | 5 - StatusTransaction | Equipo de integraciones de FLIT |
| 1.0.0 | 16/05/2025 | 9 - Secretaries | Equipo de integraciones de FLIT |
| 1.0.0 | 31/07/2025 | 10 - PauseDraftProcess | Equipo de integraciones de FLIT |
| 1.0.0 | 31/07/2025 | 11 - AbortProcess | Equipo de integraciones de FLIT |
| 3.0.0 | 06/10/2025 | 2 - Transacction | Equipo de integraciones de FLIT |
| 1.1.0 | sin fecha | — | Equipo de integraciones de FLIT |

## Saneamiento

Lo sensible que trae el Excel (se cuenta, no se copia):

| Tipo | Apariciones en el Excel |
|---|---|
| Valores después de `Bearer` | 1 |
| Hojas con el nombre de un colaborador (el changelog lo deja genérico) | 7 |
| Correos de dominios reales | 13 |
| Hosts internos | 3 |
| Tokens con forma de JWT | 2 |
| Rutas locales de Windows | 1 |

Lo que se reemplazó al llevar ejemplos y textos al spec (lo que no se usa, como las cabeceras del curl, no llega a ningún archivo):

| Qué se reemplazó | Veces |
|---|---|
| Correos → `usuario@ejemplo.com` | 10 |
| Contraseñas y tokens por nombre de campo → `$CLAVE` / `$TOKEN` | 2 |
| Números de documento → `900123456` | 12 |
| Nombres de empresas → `Organismo Demo` | 3 |
| Nombres de personas → `Nombre Demo` / `Apellido Demo` | 28 |
| Placas → `ABC123` | 3 |
| Rutas locales de equipos personales → `documento.pdf` | 1 |
| Teléfonos → `3001234567` | 5 |
| VIN → `VIN00000000000001` | 1 |
