"""Qué hoja del Excel corresponde a qué operación de core-ict (FLIT 2), y las decisiones que no se deducen solas.

La base es `contracts/openapi/core-ict.v1.yaml` del repo `flit`. Cada decisión que no sale de ese contrato ni del
Excel (alias de campos, cuerpos tomados del Excel, operaciones que core-ict implementa y su contrato no declara,
correcciones verificadas en el código de `services/core-ict`) tiene su motivo aquí y llega al informe
(ATLAS-ADR-0037).
"""

from dataclasses import dataclass, field

# Archivos de la base dentro del repo `flit` (rutas relativas: nunca llegan rutas locales a la salida).
CONTRATO_BASE = "contracts/openapi/core-ict.v1.yaml"
GATEWAY = "services/core-api/src/Flit.Gateway/appsettings.json"
CLUSTER_ICT = "core-ict-cluster"

# Nombres de la salida en contracts/ict/.
ARCHIVO_SPEC = "core-ict.enriquecido.v1.yaml"
ARCHIVO_PROPUESTA = "propuesta-flit/core-ict.v1.yaml"

CODIGO = "core-ict"  # nombre del contrato en Atlas
APLICATIVO = "flit2-core-ict"  # slug del catálogo de Atlas [por verificar]
ESQUEMA_CLIENTE = "ictClientToken"  # esquema de seguridad del token propio del cliente ICT


@dataclass(frozen=True)
class Destino:
    hoja: str  # servicio de la hoja («Transacction» en «2 - Transacction»)
    metodo: str | None = None
    ruta: str | None = None
    webhook: str | None = None
    alias: dict[str, str] = field(default_factory=dict)  # campo o segmento del Excel → propiedad del spec
    cuerpo_desde_excel: str | None = None  # motivo para no usar el esquema del contrato en la petición
    componente_peticion: str | None = None
    componente_respuesta: str | None = None
    media_peticion: str | None = None
    tipos_forzados: dict[str, dict] = field(default_factory=dict)
    sin_equivalente: str | None = None  # motivo por el que la hoja no tiene operación en core-ict
    nota_ruta: str | None = None


_FILA_REGISTRO = (
    "el contrato declara un objeto `{ transactions: [...] }` con filas sin propiedades; core-ict "
    "(`IctRegisterEndpoints.RegisterAsync`) lee un arreglo JSON de filas `RegisterRowInput` en snake_case con "
    "`seller`, `buyer` y `lessee`, así que el esquema de la petición sale del Excel"
)

DESTINOS: list[Destino] = [
    Destino("Auth", "post", "/api/v1/auth/login", alias={"Usuario": "username"}),
    Destino(
        "Transacction",
        "post",
        "/api/v1/external-transaction/register",
        alias={"UrlWebHook": "url_web_hook"},
        cuerpo_desde_excel=_FILA_REGISTRO,
        componente_peticion="SolicitudRegistroTramite",
    ),
    Destino(
        "Attachments",
        "post",
        "/api/v1/transact-attachments/upload",
        componente_peticion="SolicitudCargaAdjunto",
        componente_respuesta="RespuestaCargaAdjunto",
        media_peticion="multipart/form-data",
        tipos_forzados={"file": {"type": "string", "contentMediaType": "application/pdf"}},
    ),
    Destino(
        "WebHookTransaction",
        webhook="estadoTramite",
        componente_peticion="NotificacionEstadoTramite",
        componente_respuesta="RespuestaNotificacion",
    ),
    Destino(
        "StatusTransaction",
        "get",
        "/api/v1/status-process/byId/{id}",
        alias={"StatusProccess": "statusProcess"},
        componente_respuesta="StatusProcessV1Response",
    ),
    Destino(
        "SearchPlate",
        sin_equivalente="core-ict no implementa la consulta de placas (ni en su contrato ni en su código)",
    ),
    Destino(
        "WebHookPlate",
        sin_equivalente="core-ict no envía notificaciones de placas: depende de SearchPlate, que no tiene equivalente",
    ),
    Destino(
        "StatusPlate",
        sin_equivalente="core-ict no implementa el estado de placas (ni en su contrato ni en su código)",
    ),
    Destino("Secretaries", "get", "/api/v1/secretaries", componente_respuesta="ListadoSecretarias"),
    Destino("PauseDraftProcess", "put", "/api/v1/external-transaction/pauseDraftProcess/{id}"),
    Destino("AbortProcess", "put", "/api/v1/external-transaction/abortProcess/{id}"),
]

# --- Operaciones que core-ict implementa (rutas de compatibilidad v1) y su contrato no declara -------------------
# Se verificaron en el código de `services/core-ict/src/Flit.Ict.Api/Endpoints/` del repo `flit`: método, ruta,
# seguridad (`IctClientPolicy`), cuerpo y códigos de respuesta. La conversión las agrega al spec con
# `x-flit-origen` y el informe las lista para que el contrato de `flit` las declare.


def _mensaje_v1(nombre: str, descripcion: str) -> dict:
    return {
        "description": descripcion,
        "content": {"application/json": {"schema": {"$ref": f"#/components/schemas/{nombre}"}}},
    }


def _parametro_id(descripcion: str) -> dict:
    return {"name": "id", "in": "path", "required": True, "description": descripcion, "schema": {"type": "string"}}


_ID_TRAMITE = "TransactionFlit (manager_id_transaction) que devolvió el registro."

OPERACIONES_DEL_CODIGO: dict[tuple[str, str], dict] = {
    ("post", "/api/v1/transact-attachments/upload"): {
        "fuente": "IctAttachmentEndpoints.cs (MapV1TransactAttachments)",
        "operacion": {
            "tags": ["ICT · Adjuntos"],
            "summary": "Carga de un documento adjunto del trámite",
            "description": (
                "Recibe un archivo del trámite por `multipart/form-data` (hasta 5 MB). Con `closed=true` el gestor "
                "indica que no enviará más documentos."
            ),
            "security": [{ESQUEMA_CLIENTE: []}],
            "requestBody": {
                "required": True,
                "content": {"multipart/form-data": {"schema": {"$ref": "#/components/schemas/SolicitudCargaAdjunto"}}},
            },
            "responses": {
                "201": _mensaje_v1("RespuestaCargaAdjunto", "Documento registrado."),
                "400": _mensaje_v1("RespuestaCargaAdjunto", "Estructura de petición inválida."),
                "401": _mensaje_v1("RespuestaCargaAdjunto", "Token ICT ausente o inválido."),
                "404": _mensaje_v1("RespuestaCargaAdjunto", "Trámite no encontrado."),
                "409": _mensaje_v1("RespuestaCargaAdjunto", "El documento del trámite ya está cerrado."),
                "422": _mensaje_v1("RespuestaCargaAdjunto", "El archivo excede el tamaño permitido (5 MB)."),
            },
        },
        "esquemas": {
            "SolicitudCargaAdjunto": {
                "type": "object",
                "required": ["transactionFlit", "filename", "idAttachment", "file"],
                "properties": {
                    "transactionFlit": {"type": "string"},
                    "filename": {"type": "string"},
                    "idAttachment": {"type": "integer"},
                    "closed": {"type": "boolean"},
                    "file": {"type": "string", "contentMediaType": "application/pdf"},
                },
            },
            "RespuestaCargaAdjunto": {
                "type": "object",
                "required": ["Status", "Message"],
                "properties": {
                    "Status": {"type": "integer", "description": "1 = registrado, 0 = error."},
                    "Message": {"type": "string"},
                },
            },
        },
    },
    ("get", "/api/v1/secretaries"): {
        "fuente": "IctSecretariesEndpoints.cs",
        "operacion": {
            "tags": ["ICT · Secretarías"],
            "summary": "Secretarías de tránsito activas",
            "description": "Lista las secretarías de tránsito activas con su código (claves `Code` y `Name`).",
            "security": [{ESQUEMA_CLIENTE: []}],
            "responses": {
                "200": _mensaje_v1("ListadoSecretarias", "Secretarías activas."),
                "401": {"description": "Token ICT ausente o inválido."},
            },
        },
        "esquemas": {
            "ListadoSecretarias": {
                "type": "object",
                "required": ["data"],
                "properties": {
                    "data": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "required": ["Code", "Name"],
                            "properties": {"Code": {"type": "string"}, "Name": {"type": "string"}},
                        },
                    }
                },
            }
        },
    },
    ("put", "/api/v1/external-transaction/pauseDraftProcess/{id}"): {
        "fuente": "IctLifecycleEndpoints.cs (PauseAsync)",
        "operacion": {
            "tags": ["ICT · Ciclo de vida"],
            "summary": "Pausar o reanudar un trámite en borrador",
            "description": "Pausa (`pauseProcess=true`) o reanuda el trámite ya materializado en borrador.",
            "security": [{ESQUEMA_CLIENTE: []}],
            "parameters": [_parametro_id(_ID_TRAMITE)],
            "requestBody": {
                "required": True,
                "content": {"application/json": {"schema": {"$ref": "#/components/schemas/SolicitudPausaTramite"}}},
            },
            "responses": {
                "200": _mensaje_v1("RespuestaCicloVidaV1", "Trámite pausado o reanudado."),
                "401": _mensaje_v1("RespuestaCicloVidaV1", "No autorizado."),
                "403": _mensaje_v1("RespuestaCicloVidaV1", "El trámite no está en borrador."),
                "404": _mensaje_v1("RespuestaCicloVidaV1", "El trámite no existe o aún no está en borrador."),
                "503": _mensaje_v1("RespuestaCicloVidaV1", "El servicio de trámites no está disponible."),
            },
        },
        "esquemas": {
            "SolicitudPausaTramite": {
                "type": "object",
                "required": ["pauseProcess"],
                "properties": {"pauseProcess": {"type": "boolean"}, "observation": {"type": ["string", "null"]}},
            },
        },
    },
    ("put", "/api/v1/external-transaction/abortProcess/{id}"): {
        "fuente": "IctLifecycleEndpoints.cs (AbortAsync)",
        "operacion": {
            "tags": ["ICT · Ciclo de vida"],
            "summary": "Anular un trámite en borrador o rechazado",
            "description": "Anula el trámite ya materializado si está en borrador o rechazado.",
            "security": [{ESQUEMA_CLIENTE: []}],
            "parameters": [_parametro_id(_ID_TRAMITE)],
            "requestBody": {
                "required": False,
                "content": {"application/json": {"schema": {"$ref": "#/components/schemas/SolicitudAnulacionTramite"}}},
            },
            "responses": {
                "200": _mensaje_v1("RespuestaCicloVidaV1", "Trámite anulado."),
                "401": _mensaje_v1("RespuestaCicloVidaV1", "No autorizado."),
                "403": _mensaje_v1("RespuestaCicloVidaV1", "El trámite no está en un estado anulable."),
                "404": _mensaje_v1("RespuestaCicloVidaV1", "El trámite no existe o aún no está en borrador."),
                "409": _mensaje_v1("RespuestaCicloVidaV1", "Conflicto de concurrencia al anular el trámite."),
                "503": _mensaje_v1("RespuestaCicloVidaV1", "El servicio de trámites no está disponible."),
            },
        },
        "esquemas": {
            "SolicitudAnulacionTramite": {
                "type": "object",
                "properties": {"observation": {"type": ["string", "null"]}},
            },
        },
    },
}

# Esquema compartido de las respuestas de pausa y anulación (`LifecycleV1Result`).
ESQUEMAS_COMUNES: dict[str, dict] = {
    "RespuestaCicloVidaV1": {
        "type": "object",
        "required": ["success", "message"],
        "properties": {
            "success": {"type": "boolean"},
            "message": {"type": "string"},
            "data": {"type": "null"},
        },
    },
}

# Esquema del webhook según `WebhookNotificationJob.TryDeliverAsync`: campos v2 y los de compatibilidad v1.
WEBHOOK_ESTADO_TRAMITE: dict = {
    "fuente": "Flit.Ict.Infrastructure/Jobs/WebhookNotificationJob.cs (TryDeliverAsync)",
    "esquema": {
        "type": "object",
        "required": ["managerIdTransaction", "ictEstado", "transactionFlit", "status"],
        "properties": {
            "managerIdTransaction": {"type": "string"},
            "ictEstado": {
                "type": "string",
                "enum": [
                    "recibido",
                    "en_validacion_negocio",
                    "en_validacion_externa",
                    "procesado",
                    "borrador_creado",
                    "con_novedades",
                    "anulado",
                ],
                "description": "Estado del pre-trámite en el vocabulario v2 de core-ict.",
            },
            "procedureInstanceId": {"type": ["string", "null"]},
            "transactionType": {"type": "integer"},
            "message": {"type": "string"},
            "timestamp": {"type": "string", "format": "date-time"},
            "transactionFlit": {"type": "string"},
            "status": {"type": "integer"},
            "statusDescription": {"type": "string"},
            "statusMessage": {"type": "string"},
            "statusObservation": {"type": "string"},
            "statusText": {"type": "string"},
        },
    },
    "notas": [
        "core-ict no envía cabecera `Authorization` en la notificación (el Excel dice `Bearer`); el gestor no puede "
        "autenticar a FLIT por token. Confirmar si se agrega una firma o un token",
        "core-ict solo mira el código HTTP de la respuesta del gestor (2xx = entregado); el cuerpo que documenta el "
        "Excel en «Salida» no se lee",
    ],
}

# --- Correcciones al contrato verificadas en el código de core-ict -----------------------------------------------
# (puntero JSON en el contrato ya convertido a 3.1, valor nuevo, motivo). Se aplican antes de enriquecer.
CORRECCIONES: list[tuple[str, object, str]] = [
    (
        "/components/schemas/StatusProcessV1Response/properties/statusProcess/items",
        {
            "type": "object",
            "properties": {
                "status": {"type": "integer"},
                "statusDescription": {"type": "string"},
                "observation": {"type": "string"},
                "userMailRegistered": {"type": "string"},
                "userRoleRegistered": {"type": "string"},
                "userNameRegistered": {"type": "string"},
                "dateRegistered": {"type": "string"},
                "companyRegistered": {"type": "string"},
            },
        },
        "`StatusProcessV1Item` (`Flit.Ict.Domain/Abstractions/IStatusProcessV1Query.cs`) define los ítems del "
        "histórico (camelCase al serializar); el contrato los dejaba como objeto sin propiedades",
    ),
    (
        "/components/schemas/IctLoginResponse/properties",
        {
            "token": {"type": "string"},
            "expiresInSeconds": {"type": "integer"},
            "mustRotate": {"type": "boolean"},
        },
        "`IctAuthEndpoints.LoginAsync` devuelve `{ token, expiresInSeconds }` (o `{ mustRotate: true }`); el contrato "
        "declaraba `expiresAt`",
    ),
    (
        "/paths/~1api~1v1~1auth~1login/post/responses/403",
        {
            "description": "Cliente inactivo, bloqueado o sin compañía disponible (`inactive`, `locked`, "
            "`tenant_unavailable`)."
        },
        "`IctAuthEndpoints.LoginAsync` responde 403 a un cliente inactivo o bloqueado; el contrato solo declaraba 401",
    ),
    (
        "/paths/~1api~1v1~1external-transaction~1register/post/responses/400",
        {"description": "Cuerpo vacío o que no es un arreglo de filas (`invalid_body`, `empty_body`)."},
        "`IctRegisterEndpoints.RegisterAsync` responde 400 si el cuerpo no se puede leer; el contrato no lo declaraba",
    ),
]

# Campos del Excel que core-ict acepta pero no usa.
CAMPOS_IGNORADOS: dict[tuple[str, str], str] = {
    ("Transacction", "company_manager_id"): (
        "core-ict deriva la compañía del token y no lee `company_manager_id` (`RegisterRowInput` no lo declara); se "
        "documenta como obsoleto"
    ),
}

# Tablas de referencia del Excel → componente reutilizable. «valor» es la columna que viaja en la API.
TABLAS: dict[str, dict] = {
    "tabla tipo documento": {
        "componente": "TipoDocumento",
        "valor": "document_type_code",
        "tipo": "string",
        "campos": [
            "document_type",
            "legal_representative_document_type",
            "principal_mandante_document_type",
            "limitations_creditor_document_type",
        ],
    },
    "tabla tipo operacion": {"componente": "TipoOperacion", "valor": "ID", "tipo": "integer"},
    "tabla tipo transformacion": {
        "componente": "TipoTransformacion",
        "valor": "id_transformation_type",
        "tipo": "integer",
    },
    "tabla tipo tramite": {"componente": "TipoTramite", "valor": "ID", "tipo": "integer"},
    "tabla tipo operacion garantia": {"componente": "TipoOperacionGarantia", "valor": "ID", "tipo": "integer"},
    "tabla tipo de blindaje": {"componente": "TipoBlindaje", "valor": "ID", "tipo": "integer"},
    "tabla nuevo tipo de combustible": {"componente": "TipoCombustible", "valor": "ID", "tipo": "integer"},
    "tabla guia documentos adjuntos": {
        "componente": "DocumentoAdjunto",
        "valor": "ID",
        "tipo": "integer",
        "campos": ["idAttachment"],
    },
}

# Tablas que se dejan fuera del spec a propósito.
TABLAS_OMITIDAS: dict[str, str] = {
    "tabla para guardar los ids de los archivos adjuntos": "diseño interno de persistencia; no es parte del contrato",
}

# Columnas de obligatoriedad de la hoja de transacciones → tipo de trámite (Tabla Tipo Tramite).
TIPOS_TRAMITE_OBLIGATORIEDAD: dict[str, dict] = {
    "TRASPASO BILATERAL": {"clave": "traspaso_bilateral", "componente": "TramiteTraspasoBilateral", "valor": 3},
    "TRASPASO UNILATERAL": {"clave": "traspaso_unilateral", "componente": "TramiteTraspasoUnilateral", "valor": 4},
    "MATRICULA INICIAL": {"clave": "matricula_inicial", "componente": "TramiteMatriculaInicial", "valor": 1},
    "MATRICULA TIPO LEASING": {"clave": "matricula_leasing", "componente": "TramiteMatriculaLeasing", "valor": 2},
}

# Columnas de la guía de adjuntos → tipo de trámite.
COLUMNAS_GUIA_ADJUNTOS: dict[str, str] = {
    "MI": "matricula_inicial",
    "MI LEASING": "matricula_leasing",
    "TR BILATERAL": "traspaso_bilateral",
    "TR UNILATERAL": "traspaso_unilateral",
    "Otro Trámite": "otro_tramite",
}

# Errores de nombre conocidos en el Excel y en el contrato (para el informe; el spec conserva el nombre real).
NOMBRES_CON_ERROR: dict[str, str] = {
    "Transacction": "Transaction",
    "Proccess": "Process",
    "Firts": "First",
    "Representate": "Representante",
    "crecion": "creación",
    "trasaccion": "transacción",
}

# Mensajes de los ejemplos de error → código HTTP.
MENSAJES_CODIGO: list[tuple[str, int]] = [
    ("not found", 404),
    ("no encontrad", 404),
    ("unexpected", 500),
    ("inesperad", 500),
    ("token inválido", 401),
    ("expirado", 401),
    ("incorrect username or password", 401),
    ("estructura de petición inválida", 400),
]

# Hosts públicos admitidos en `servers`. Todo host que no coincida va al informe y no al spec.
HOSTS_PUBLICOS = r"^[a-z0-9-]+(?:\.[a-z0-9-]+)*\.flitsas\.(?:online|com)$"

# Ambientes que se publican a clientes: QA (pruebas de integración) y producción. Dev es de uso interno de FLIT.
AMBIENTES_PUBLICADOS: dict[str, str] = {
    "qa": "QA (pruebas de integración). La consola «probar ahora» de Atlas solo apunta aquí o al sandbox.",
    "pdn": "Producción.",
}
