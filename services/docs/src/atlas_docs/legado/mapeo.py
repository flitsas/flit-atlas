"""Qué hoja del Excel corresponde a qué operación de qué repo legado, y las decisiones que no se deducen solas.

Cada decisión que no sale del código ni del Excel tiene su motivo aquí y llega al informe (ATLAS-ADR-0037).
"""

from dataclasses import dataclass, field


@dataclass(frozen=True)
class Api:
    repo: str
    codigo: str  # nombre del servicio en flit-ci (service-name) y del contrato en Atlas
    aplicativo: str  # slug del catálogo de Atlas
    titulo: str
    descripcion: str
    hoja_version: str | None = None  # hoja del Excel que fija info.version


@dataclass(frozen=True)
class Destino:
    hoja: str  # servicio de la hoja («Transacction» en «2 - Transacction»)
    repo: str | None
    metodo: str | None = None
    ruta: str | None = None
    webhook: str | None = None
    alias: dict[str, str] = field(default_factory=dict)  # campo del Excel → propiedad del spec
    cuerpo_desde_excel: str | None = None  # motivo para no usar el esquema de tsoa de la petición
    componente_peticion: str | None = None
    componente_respuesta: str | None = None
    media_peticion: str | None = None
    tipos_forzados: dict[str, dict] = field(default_factory=dict)
    pendiente: str | None = None  # motivo por el que no se publica a clientes todavía
    nota_ruta: str | None = None


APIS: list[Api] = [
    Api(
        "BackApiExternalAuth",
        "external-auth",
        "legado-back-api-external-auth",
        "API externa ICT · Autenticación",
        "Autenticación de los gestores externos: entrega el token que piden las demás operaciones de la API ICT.",
        "Auth",
    ),
    Api(
        "BackApiExternalTransact",
        "external-transact",
        "legado-back-api-external-transact",
        "API externa ICT · Trámites",
        "Registro, pausa y anulación de trámites de gestores externos y consulta de secretarías de tránsito.",
        "Transacction",
    ),
    Api(
        "BackApiExternalTransactAttach",
        "external-transactattach",
        "legado-back-api-external-transact-attach",
        "API externa ICT · Adjuntos de trámites",
        "Carga de los documentos adjuntos de un trámite según la guía de documentos por tipo de trámite.",
        "Attachments",
    ),
    Api(
        "BackApiExternalTransactStatus",
        "external-transactstatus",
        "legado-back-api-external-transact-status",
        "API externa ICT · Estado de trámites",
        "Consulta del estado de validación y del historial de estados de un trámite.",
        "StatusTransaction",
    ),
    Api(
        "BackApiExternalTransactWebHook",
        "external-transactwebhook",
        "legado-back-api-external-transact-webhook",
        "API externa ICT · Webhooks",
        "Notificaciones que FLIT envía a la URL del gestor cuando cambia el estado de un trámite o de una placa.",
        "WebHookTransaction",
    ),
    Api(
        "BackApiExternalAttachments",
        "external-attachments",
        "legado-back-api-external-attachments",
        "API externa ICT · Archivos (interna)",
        "Almacenamiento de archivos y URL prefirmadas que usan los demás servicios de la API ICT. Uso interno.",
    ),
    Api(
        "BackApiExternalLog",
        "external-log",
        "legado-back-api-external-log",
        "API externa ICT · Registro de auditoría (interna)",
        "Registro de las transacciones y de los ingresos de la API ICT. Uso interno.",
    ),
    Api(
        "BackApiExternalTransactValiQueryExt",
        "external-valiqueryext",
        "legado-back-api-external-transact-vali-query-ext",
        "API externa ICT · Orquestador de validaciones (interna)",
        "Procesamiento de las transacciones pendientes de validación y consulta en fuentes externas. Uso interno.",
    ),
]

_DTO_INTERNO = (
    "el DTO de tsoa (`ExternalIntegrationMasterRequestDto`) describe el modelo interno en camelCase con `actors`; la "
    "API recibe snake_case con `seller`, `buyer` y `lessee` y lo transforma con el mapeador `ToCamelCase` del repo, "
    "así que el esquema de la petición sale del Excel"
)

DESTINOS: list[Destino] = [
    Destino("Auth", "BackApiExternalAuth", "post", "/api/v1/auth/login", alias={"Usuario": "username"}),
    Destino(
        "Transacction",
        "BackApiExternalTransact",
        "post",
        "/api/v1/external-transaction/register",
        alias={"UrlWebHook": "url_web_hook"},
        cuerpo_desde_excel=_DTO_INTERNO,
        componente_peticion="SolicitudRegistroTramite",
    ),
    Destino(
        "Attachments",
        "BackApiExternalTransactAttach",
        "post",
        "/api/v1/transact-attachments/upload",
        cuerpo_desde_excel=(
            "tsoa declara `application/json` con un esquema vacío; el controlador recibe `multipart/form-data` con "
            "multer, como muestra el curl del Excel"
        ),
        componente_peticion="SolicitudCargaAdjunto",
        componente_respuesta="RespuestaCargaAdjunto",
        media_peticion="multipart/form-data",
        tipos_forzados={"file": {"type": "string", "contentMediaType": "application/pdf"}},
    ),
    Destino(
        "WebHookTransaction",
        "BackApiExternalTransactWebHook",
        webhook="estadoTramite",
        componente_peticion="NotificacionEstadoTramite",
        componente_respuesta="RespuestaNotificacion",
    ),
    Destino(
        "StatusTransaction",
        "BackApiExternalTransactStatus",
        "get",
        "/api/v1/status-process/transaction/{id}",
        componente_respuesta="EstadoTramite",
        nota_ruta=(
            "el Excel documenta `/api/v1/status-process/byId/{id}`, que no existe en el swagger; se asocia a "
            "`/api/v1/status-process/transaction/{id}` (método `getStatusProcessById` del servicio)"
        ),
    ),
    Destino(
        "SearchPlate",
        None,
        pendiente="ningún `BackApiExternal*` expone una ruta de consulta de placas y la hoja no trae URL",
    ),
    Destino(
        "WebHookPlate",
        "BackApiExternalTransactWebHook",
        webhook="estadoPlaca",
        componente_peticion="NotificacionEstadoPlaca",
        componente_respuesta="RespuestaNotificacion",
        pendiente=(
            "depende de SearchPlate, que no tiene servicio verificado; queda interno hasta que el líder lo confirme"
        ),
    ),
    Destino(
        "StatusPlate",
        None,
        pendiente="ningún `BackApiExternal*` expone una ruta de estado de placas y la hoja no trae URL",
    ),
    Destino(
        "Secretaries",
        "BackApiExternalTransact",
        "get",
        "/api/v1/secretaries",
        componente_respuesta="ListadoSecretarias",
    ),
    Destino(
        "PauseDraftProcess", "BackApiExternalTransact", "put", "/api/v1/external-transaction/pauseDraftProcess/{id}"
    ),
    Destino("AbortProcess", "BackApiExternalTransact", "put", "/api/v1/external-transaction/abortProcess/{id}"),
]

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

# Errores de nombre conocidos en el Excel y en el código (para el informe; el spec conserva el nombre real del campo).
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

# Hosts públicos admitidos en `servers`. Todo host del Excel que no coincida va al informe y no al spec.
HOSTS_PUBLICOS = r"^[a-z0-9-]+(?:\.[a-z0-9-]+)*\.flitsas\.(?:online|com)$"
