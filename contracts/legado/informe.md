# Informe de la conversión del legado ICT

Generado por `python -m atlas_docs.legado` (HU 3.8, ATLAS-ADR-0037) desde el swagger de tsoa de cada `BackApiExternal*` y el Excel `Contratos_Integraciones_31Jul2025.xlsx`. No se edita a mano: se regenera junto con los specs. No trae valores sensibles: los ejemplos y los hosts internos se describen sin su valor.

## Resumen

| Contrato | Repo | Versión | Visibilidad | Operaciones públicas | Operaciones internas | Webhooks |
|---|---|---|---|---|---|---|
| `external-auth` | `BackApiExternalAuth` | 1.5.0 | compartible | 1 | 8 | 0 |
| `external-transact` | `BackApiExternalTransact` | 3.0.0 | compartible | 4 | 22 | 0 |
| `external-transactattach` | `BackApiExternalTransactAttach` | 1.8.0 | compartible | 1 | 5 | 0 |
| `external-transactstatus` | `BackApiExternalTransactStatus` | 1.7.0 | compartible | 1 | 3 | 0 |
| `external-transactwebhook` | `BackApiExternalTransactWebHook` | 1.0.0 | compartible | 0 | 2 | 2 |
| `external-attachments` | `BackApiExternalAttachments` | 1.0.0 | interna | 0 | 6 | 0 |
| `external-log` | `BackApiExternalLog` | 1.0.0 | interna | 0 | 4 | 0 |
| `external-valiqueryext` | `BackApiExternalTransactValiQueryExt` | 1.0.0 | interna | 0 | 3 | 0 |

## Inconsistencias por tipo

| Tipo | Cantidad |
|---|---|
| [Hosts mezclados](#hosts-mezclados) | 5 |
| [URL de dev o pdn vacías](#url-de-dev-o-pdn-vacías) | 11 |
| [Servicios del índice sin hoja](#servicios-del-índice-sin-hoja) | 2 |
| [Salidas incompletas](#salidas-incompletas) | 4 |
| [Hojas sin operación en el código](#hojas-sin-operación-en-el-código) | 3 |
| [Rutas del Excel que no coinciden con el swagger](#rutas-del-excel-que-no-coinciden-con-el-swagger) | 1 |
| [Nombres con errores](#nombres-con-errores) | 7 |
| [Esquemas de tsoa que no describen la API real](#esquemas-de-tsoa-que-no-describen-la-api-real) | 6 |
| [Tipos distintos entre el Excel y el código](#tipos-distintos-entre-el-excel-y-el-código) | 2 |
| [Obligatoriedad distinta entre el Excel y el código](#obligatoriedad-distinta-entre-el-excel-y-el-código) | 3 |
| [Ejemplo y tabla de campos no coinciden](#ejemplo-y-tabla-de-campos-no-coinciden) | 5 |
| [Ejemplos reparados o ajustados al esquema](#ejemplos-reparados-o-ajustados-al-esquema) | 11 |
| [Seguridad declarada distinta](#seguridad-declarada-distinta) | 9 |
| [Tablas de referencia con datos dudosos u omitidas](#tablas-de-referencia-con-datos-dudosos-u-omitidas) | 6 |
| [Códigos de respuesta](#códigos-de-respuesta) | 9 |
| [Campos que exige el código y el Excel no documenta](#campos-que-exige-el-código-y-el-excel-no-documenta) | 1 |
| [operationId duplicados en el swagger](#operationid-duplicados-en-el-swagger) | 10 |
| [Metadatos de tsoa y del Excel](#metadatos-de-tsoa-y-del-excel) | 28 |
| **Total** | **123** |

## Pendientes para el líder técnico

- [por verificar] Confirmar las URL vigentes de dev, qa y pdn de cada API: el Excel solo trae QA y mezcla hosts (no se adivinaron; los specs solo declaran el host de QA del Excel).
- [por verificar] Decidir si RequestSign y StatusSign existen (están en el índice y no tienen hoja).
- [por verificar] Confirmar el servicio y la ruta de SearchPlate y StatusPlate, y si WebHookPlate se publica.
- [por verificar] Confirmar si los specs enriquecidos viven en cada repo legado o en un repo de contratos legados (§8.4, punto 9) y aprobar los PR propuestos en `propuestas/`.
- [por verificar] Revisar los campos que el código exige y el Excel no documenta, y los tipos que no coinciden.

## Hosts mezclados

| Contrato | Hoja | Detalle |
|---|---|---|
| — | — | `*.execute-api.us-east-1.amazonaws.com (API Gateway de AWS; id omitido)` en «3 - Attachments» |
| — | — | `ict.qa.flitsas.online` en «1 - Auth», «10 - PauseDraftProcess», «11 - AbortProcess», «2 - Transacction», «3 - Attachments», «5 - StatusTransaction», «9 - Secretaries» |
| — | — | `servidor local de pruebas (mock)` en «5 - StatusTransaction» |
| — | — | `status.transact.api.qa.flitsas.online` en «5 - StatusTransaction» |
| — | — | `transact.api.qa.flitsas.online` en «10 - PauseDraftProcess», «11 - AbortProcess», «9 - Secretaries» |

## URL de dev o pdn vacías

| Contrato | Hoja | Detalle |
|---|---|---|
| — | 1 - Auth | sin URL de dev, pdn |
| — | 2 - Transacction | sin URL de dev, pdn |
| — | 3 - Attachments | sin URL de dev, pdn |
| — | 4 - WebHookTransaction | sin URL de dev, qa, pdn |
| — | 7 - WebHookPlate | sin URL de dev, qa, pdn |
| — | 5 - StatusTransaction | sin URL de dev, pdn |
| — | 6 - SearchPlate | sin URL de dev, qa, pdn |
| — | 8 - StatusPlate | sin URL de dev, qa, pdn |
| — | 9 - Secretaries | sin URL de dev, pdn |
| — | 10 - PauseDraftProcess | sin URL de dev, pdn |
| — | 11 - AbortProcess | sin URL de dev, pdn |

## Servicios del índice sin hoja

| Contrato | Hoja | Detalle |
|---|---|---|
| — | — | «RequestSign» está en el índice («Servicio de solicitud de validacion de identidad») y no tiene hoja |
| — | — | «StatusSign» está en el índice («Servicio de consulta de estado de una validacion de identidad») y no tiene hoja |

## Salidas incompletas

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transact` | 10 - PauseDraftProcess | `pauseProcess` está bajo «Salida» y es un campo de la petición; la respuesta real (`UpdatePauseDraftProcess`) no está documentada en el Excel |
| `external-transact` | 10 - PauseDraftProcess | `observation` está bajo «Salida» y es un campo de la petición; la respuesta real (`UpdatePauseDraftProcess`) no está documentada en el Excel |
| `external-transact` | 11 - AbortProcess | `observation` está bajo «Salida» y es un campo de la petición; la respuesta real (`RegisterAbortProcess`) no está documentada en el Excel |
| — | 6 - SearchPlate | la salida solo confirma el registro de la placa; el estado y los gastos no tienen salida documentada en esta hoja (§8.4, punto 8) |

## Hojas sin operación en el código

| Contrato | Hoja | Detalle |
|---|---|---|
| — | 6 - SearchPlate | ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| — | 7 - WebHookPlate | depende de SearchPlate, que no tiene servicio verificado; queda interno hasta que el líder lo confirme |
| — | 8 - StatusPlate | ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |

## Rutas del Excel que no coinciden con el swagger

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transactstatus` | 5 - StatusTransaction | el Excel documenta `/api/v1/status-process/byId/{id}`, que no existe en el swagger; se asocia a `/api/v1/status-process/transaction/{id}` (método `getStatusProcessById` del servicio) |

## Nombres con errores

| Contrato | Hoja | Detalle |
|---|---|---|
| — | — | `Firts` en ejemplos de «2 - Transacction»: «Firts» por «First». El spec conserva el nombre que usa la API |
| — | — | `Representate` en descripciones de «2 - Transacction»: «Representate» por «Representante». El spec conserva el nombre que usa la API |
| — | — | `StatusProccess` en campos de «5 - StatusTransaction», campos de «8 - StatusPlate», ejemplos de «5 - StatusTransaction», ejemplos de «8 - StatusPlate»: «Proccess» por «Process». El spec conserva el nombre que usa la API |
| — | — | `Transacction` en nombre de hoja, índice del Excel: «Transacction» por «Transaction». El spec conserva el nombre que usa la API |
| — | — | `crecion` en descripciones de «8 - StatusPlate»: «crecion» por «creación». El spec conserva el nombre que usa la API |
| — | — | `trasaccion` en descripciones de «2 - Transacction»: «trasaccion» por «transacción». El spec conserva el nombre que usa la API |
| — | — | `updateStatusProccess` en rutas de `BackApiExternalTransact`: «Proccess» por «Process». El spec conserva el nombre que usa la API |

## Esquemas de tsoa que no describen la API real

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-auth` | 1 - Auth | `LoginService`: tsoa deja vacío el esquema de la respuesta 200 |
| `external-transact` | 2 - Transacction | `CreateNewList`: el DTO de tsoa (`ExternalIntegrationMasterRequestDto`) describe el modelo interno en camelCase con `actors`; la API recibe snake_case con `seller`, `buyer` y `lessee` y lo transforma con el mapeador `ToCamelCase` del repo, así que el esquema de la petición sale del Excel |
| `external-transact` | 9 - Secretaries | `GetAvailableTrafficSecretariesEvent`: tsoa deja vacío el esquema de la respuesta 200 |
| `external-transactattach` | 3 - Attachments | `CreateAndUpload`: tsoa declara `application/json` con un esquema vacío; el controlador recibe `multipart/form-data` con multer, como muestra el curl del Excel |
| `external-transactattach` | 3 - Attachments | `CreateAndUpload`: tsoa deja vacío el esquema de la respuesta 200 |
| `external-transactstatus` | 5 - StatusTransaction | `GetStatusByid`: tsoa deja vacío el esquema de la respuesta 200 |

## Tipos distintos entre el Excel y el código

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transactattach` | 3 - Attachments | `file`: el Excel dice base64 y el servicio recibe el archivo binario (multipart) |
| `external-transactstatus` | 5 - StatusTransaction | parámetro `id` de `GetStatusByid`: Excel «number», código `string` (se conserva el del código) |

## Obligatoriedad distinta entre el Excel y el código

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transact` | 2 - Transacction | 47 reglas de obligatoriedad condicionales («si el vendedor es un NIT», «si usan mandante»…) quedan como texto en `x-flit-obligatoriedad`; no se modelan como esquema |
| `external-transact` | 10 - PauseDraftProcess | `observation`: opcional en el Excel y obligatorio en el código (manda el código) |
| `external-transact` | 11 - AbortProcess | `observation`: opcional en el Excel y obligatorio en el código (manda el código) |

## Ejemplo y tabla de campos no coinciden

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transact` | 2 - Transacction | `traffic_secretary_code`: el ejemplo trae integer y la tabla dice «String(10)» |
| `external-transact` | 2 - Transacction | campos de la tabla («parametros») que el ejemplo no trae: `armor_level_number_id`, `new_vehicle_fuel_type`, `UrlWebHook` |
| `external-transactstatus` | 5 - StatusTransaction | `StatusProccess/Status`: el ejemplo trae string y la tabla dice «Integer» |
| `external-transactwebhook` | 7 - WebHookPlate | campos de la tabla («parametros») que el ejemplo no trae: `ManagerIdTransaction` |
| `external-transactwebhook` | 7 - WebHookPlate | claves del ejemplo («parametros») que la tabla no documenta: `transactionflit` |

## Ejemplos reparados o ajustados al esquema

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-auth` | 1 - Auth | ejemplo reparado: clave con espacios sobrantes: «password» |
| `external-auth` | 1 - Auth | ejemplo reparado: comillas tipográficas cambiadas por comillas rectas |
| `external-auth` | 1 - Auth | ejemplo reparado: 3 documentos JSON seguidos en la misma celda; comillas tipográficas cambiadas por comillas rectas |
| `external-auth` | 1 - Auth | LoginService (petición): $.companyManagerId: obligatorio que faltaba (se agrega con un valor ficticio) |
| `external-transact` | 2 - Transacction | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |
| `external-transact` | 2 - Transacction | CreateNewList (petición): $[0].traffic_secretary_code: número o lógico donde se espera texto |
| `external-transact` | 9 - Secretaries | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |
| `external-transactattach` | 3 - Attachments | ejemplo reparado: 5 documentos JSON seguidos en la misma celda |
| `external-transactstatus` | 5 - StatusTransaction | GetStatusByid (respuesta): $.StatusProccess[0].Status: texto donde se espera número; $.StatusProccess[1].Status: texto donde se espera número |
| `external-transactwebhook` | 4 - WebHookTransaction | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |
| `external-transactwebhook` | 7 - WebHookPlate | ejemplo reparado: comas sobrantes antes de cerrar un objeto o una lista |

## Seguridad declarada distinta

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-auth` | — | 3 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/company-parameters`, `PUT /api/v1/company-parameters/{id}`, `DELETE /api/v1/company-parameters/{id}` |
| `external-transact` | 9 - Secretaries | `GetAvailableTrafficSecretariesEvent`: el Excel pide `Authorization: Bearer` y el swagger no declara seguridad; el spec documenta el token. Confirmar si el servicio lo exige |
| `external-transact` | — | 4 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/source-response/process-send`, `POST /api/v1/source-query`, `PUT /api/v1/source-query/{id}`, `POST /api/v1/source-query/{id}` |
| `external-transactattach` | 3 - Attachments | `CreateAndUpload` lee la cabecera `authorization` a mano (parámetro de tsoa) en lugar de declarar seguridad; el spec la declara como esquema `bearer` |
| `external-transactattach` | — | 2 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/transact-attachments/migrate-file`, `POST /api/v1/transact-attachments/associate-file` |
| `external-transactstatus` | 5 - StatusTransaction | `GetStatusByid` lee la cabecera `authorization` a mano (parámetro de tsoa) en lugar de declarar seguridad; el spec la declara como esquema `bearer` |
| `external-attachments` | — | 2 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/files`, `POST /api/v1/files/upload` |
| `external-log` | — | 2 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/transaction-log`, `POST /api/v1/auth-log` |
| `external-valiqueryext` | — | 1 operaciones internas que cambian estado no declaran seguridad: `POST /api/v1/orchestrator/processPendingTransactions` |

## Tablas de referencia con datos dudosos u omitidas

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transact` | 2 - Transacction | «Tabla Tipo Operacion Garantia»: la fila 1 trae en el ID un valor que no es número (un enlace interno); se toma 1 por la secuencia de la tabla. Confirmar |
| `external-transactattach` | 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 14 no tiene nombre en el Excel (se deja como «por confirmar») |
| `external-transactattach` | 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 15 no tiene nombre en el Excel (se deja como «por confirmar») |
| `external-transactattach` | 3 - Attachments | «Tabla guia documentos adjuntos»: el valor 16 no tiene nombre en el Excel (se deja como «por confirmar») |
| `external-transactattach` | 3 - Attachments | «Tabla para guardar los ids de los archivos adjuntos» (I27) no pasa al spec: diseño interno de persistencia; no es parte del contrato |
| — | 6 - SearchPlate | «Tabla Tipo Documento» (I6) queda fuera: la hoja no tiene operación (repite la de transacciones con códigos sin ID numérico) |

## Códigos de respuesta

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-auth` | 1 - Auth | `LoginService`: el Excel muestra un error 401 que el swagger no declara; se agrega |
| `external-auth` | 1 - Auth | `LoginService`: el Excel muestra un error 500 que el swagger no declara; se agrega |
| `external-auth` | — | códigos de la tabla del Excel que ninguna operación de este repo declara: 201, 204, 403, 409 |
| `external-transact` | — | códigos de la tabla del Excel que ninguna operación de este repo declara: 201, 204 |
| `external-transactattach` | 3 - Attachments | `CreateAndUpload`: el Excel muestra un error 400 que el swagger no declara; se agrega |
| `external-transactattach` | 3 - Attachments | `CreateAndUpload`: el Excel muestra un error 401 que el swagger no declara; se agrega |
| `external-transactattach` | — | códigos de la tabla del Excel que ninguna operación de este repo declara: 201, 204, 403, 409 |
| `external-transactstatus` | — | códigos de la tabla del Excel que ninguna operación de este repo declara: 201, 204, 403, 409 |
| `external-transactwebhook` | — | códigos de la tabla del Excel que ninguna operación de este repo declara: 201, 204, 400, 401, 403, 404, 409, 500 |

## Campos que exige el código y el Excel no documenta

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-auth` | 1 - Auth | `LoginService` exige `companyManagerId` y el Excel no lo documenta |

## operationId duplicados en el swagger

| Contrato | Hoja | Detalle |
|---|---|---|
| `external-transact` | — | `CreateNew` se repite; `POST /api/v1/source-query` pasa a `sourceQueryCreateNew` |
| `external-transact` | — | `ListPaginated` se repite; `GET /api/v1/source-query` pasa a `sourceQueryListPaginated` |
| `external-transact` | — | `UpdateOne` se repite; `PUT /api/v1/source-query/{id}` pasa a `sourceQueryUpdateOne` |
| `external-transact` | — | `DeleteOne` se repite; `POST /api/v1/source-query/{id}` pasa a `sourceQueryDeleteOne` |
| `external-transact` | — | `FindOne` se repite; `GET /api/v1/source-query/{id}` pasa a `sourceQueryFindOne` |
| `external-transact` | — | `CreateNew` se repite; `POST /api/v1/external-transaction` pasa a `externalTransactionCreateNew` |
| `external-transact` | — | `ListPaginated` se repite; `GET /api/v1/external-transaction` pasa a `externalTransactionListPaginated` |
| `external-transact` | — | `UpdateOne` se repite; `PUT /api/v1/external-transaction/{id}` pasa a `externalTransactionUpdateOne` |
| `external-transact` | — | `DeleteOne` se repite; `POST /api/v1/external-transaction/{id}` pasa a `externalTransactionDeleteOne` |
| `external-transact` | — | `FindOne` se repite; `GET /api/v1/external-transaction/{id}` pasa a `externalTransactionFindOne` |

## Metadatos de tsoa y del Excel

| Contrato | Hoja | Detalle |
|---|---|---|
| — | — | «WebHookTransaction» no tiene descripción en el índice |
| — | — | «WebHookPlate» no tiene descripción en el índice |
| — | — | la hoja «Hoja2» está vacía |
| — | 4 - WebHookTransaction | sin versión, fecha ni colaborador |
| — | 7 - WebHookPlate | sin versión, fecha ni colaborador |
| — | 6 - SearchPlate | sin versión, fecha ni colaborador |
| — | 8 - StatusPlate | sin versión, fecha ni colaborador |
| `external-auth` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-auth` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-transact` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-transact` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-transact` | — | tsoa marca como obligatorias referencias que forman un ciclo sin fin (entidad que se contiene a sí misma); se dejan opcionales: `ExternalIntegrationMasterEntity.actors` |
| `external-transactattach` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-transactattach` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-transactstatus` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-transactstatus` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-transactwebhook` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-transactwebhook` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-transactwebhook` | 4 - WebHookTransaction | la hoja «WebHookTransaction» no trae versión; el spec queda en 1.0.0 |
| `external-transactwebhook` | — | 1 esquemas de tsoa quedaron sin uso tras la conversión y se quitaron: `DefaultResponse` |
| `external-attachments` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-attachments` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-log` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-log` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-log` | — | 1 esquemas de tsoa quedaron sin uso tras la conversión y se quitaron: `DefaultResponse` |
| `external-valiqueryext` | — | 2 cabeceras espurias de tsoa (el tipo de `@SuccessResponse<T>` tomado como cabecera) se quitaron de las respuestas |
| `external-valiqueryext` | — | tsoa publica el título genérico «api_express_typescript_boilerplate», la versión 1.0.0 y un contacto personal; el spec usa el título, la versión y el contacto de la propuesta de `tsoa.json` |
| `external-valiqueryext` | — | 1 esquemas de tsoa quedaron sin uso tras la conversión y se quitaron: `DefaultResponse` |

## Campos del Excel fuera del spec

De 178 campos del Excel, 142 quedaron en un spec y 36 solo en este informe. `trazabilidad.json` dice dónde quedó cada uno (archivo y puntero JSON).

| Hoja | Celda | Sección | Campo | Motivo |
|---|---|---|---|---|
| 9 - Secretaries | A12 | parametros | `N/A` | la hoja indica que la operación no tiene parámetros |
| 6 - SearchPlate | A12 | parametros | `CompanyManagerId` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A13 | parametros | `CompanyManagerDocument` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A14 | parametros | `ManagerUser` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A15 | parametros | `ManagerMail` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A16 | parametros | `ManagerIdTransaction` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A17 | parametros | `OwnerDocumentType` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A18 | parametros | `OwnerDocumentNumber` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A19 | parametros | `Plate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A20 | parametros | `UrlWebHook` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A26 | salida | `TotalRows` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A27 | salida | `TotalRowsProcessed` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A28 | salida | `Detail/Plate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A29 | salida | `Detail/Status` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 6 - SearchPlate | A30 | salida | `Detail/Message` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL |
| 8 - StatusPlate | A12 | parametros | `CompanyManagerId` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A13 | parametros | `CompanyManagerDocument` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A14 | parametros | `Plate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A20 | salida | `StatusValidation` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A21 | salida | `MessageValidation` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A22 | salida | `StatusProccess/Status` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A23 | salida | `StatusProccess/Statusdescription` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A24 | salida | `StatusProccess/Observation` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A25 | salida | `StatusProccess/Plate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A26 | salida | `StatusProccess/Settlement/` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A27 | salida | `StatusProccess/Settlement/Type` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A28 | salida | `StatusProccess/Settlement/DescriptionType` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A29 | salida | `StatusProccess/Settlement/DescriptionGeneral` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A30 | salida | `StatusProccess/Settlement/OrderNumber` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A31 | salida | `StatusProccess/Settlement/PaymentMethod` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A32 | salida | `StatusProccess/Settlement/Amount` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A33 | salida | `StatusProccess/Settlement/CreationDate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A34 | salida | `StatusProccess/Settlement/ApprovalDate` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A35 | salida | `StatusProccess/Settlement/Bank` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A36 | salida | `StatusProccess/Settlement/CUS` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |
| 8 - StatusPlate | A37 | salida | `StatusProccess/Settlement/Franchise` | la hoja no tiene operación en el código: ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL |

## Changelog inicial

Sale de la versión, la fecha y el colaborador de cada hoja. El colaborador queda genérico: los specs se comparten con clientes y el nombre de una persona es un dato personal (Ley 1581, ATLAS-ADR-0037).

| Contrato | Versión | Fecha | Hoja | Colaborador |
|---|---|---|---|---|
| `external-auth` | 1.5.0 | 16/05/2025 | 1 - Auth | Equipo de integraciones de FLIT |
| `external-transact` | 1.0.0 | 16/05/2025 | 9 - Secretaries | Equipo de integraciones de FLIT |
| `external-transact` | 1.0.0 | 31/07/2025 | 10 - PauseDraftProcess | Equipo de integraciones de FLIT |
| `external-transact` | 1.0.0 | 31/07/2025 | 11 - AbortProcess | Equipo de integraciones de FLIT |
| `external-transact` | 3.0.0 | 06/10/2025 | 2 - Transacction | Equipo de integraciones de FLIT |
| `external-transactattach` | 1.8.0 | 16/05/2025 | 3 - Attachments | Equipo de integraciones de FLIT |
| `external-transactstatus` | 1.7.0 | 16/05/2025 | 5 - StatusTransaction | Equipo de integraciones de FLIT |

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

Lo que se reemplazó al llevar ejemplos y textos a los specs (lo que no se usa, como las cabeceras del curl, no llega a ningún archivo):

| Qué se reemplazó | Veces |
|---|---|
| Correos → `usuario@ejemplo.com` | 10 |
| Contraseñas y tokens por nombre de campo → `$CLAVE` / `$TOKEN` | 2 |
| Números de documento → `900123456` | 12 |
| Nombres de empresas → `Organismo Demo` | 3 |
| Nombres de personas → `Nombre Demo` / `Apellido Demo` | 28 |
| Placas → `ABC123` | 4 |
| Rutas locales de equipos personales → `documento.pdf` | 1 |
| Teléfonos → `3001234567` | 5 |
| VIN → `VIN00000000000001` | 1 |
