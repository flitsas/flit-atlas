"""Enriquecimiento de punta a punta con el core-ict mínimo y el xlsx ficticio de conftest.py (HU 3.8)."""

import json
from pathlib import Path

import pytest
import yaml
from conftest import COLABORADOR, CORREO_AJENO, IP_PRIVADA, RUTA_LOCAL, SENSIBLES, TOKEN_FICTICIO

from atlas_docs.ict import mapeo
from atlas_docs.ict.__main__ import ejecutar, main
from atlas_docs.ict.excel import leer_libro
from atlas_docs.ict.lenguaje import piezas_internas
from atlas_docs.ict.saneamiento import buscar_sensibles


def resolver(documento: dict, puntero: str) -> object:
    nodo: object = documento
    for parte in puntero.lstrip("/").split("/"):
        parte = parte.replace("~1", "/").replace("~0", "~")
        nodo = nodo[int(parte)] if isinstance(nodo, list) else nodo[parte]  # type: ignore[index]
    return nodo


@pytest.fixture
def salida(tmp_path: Path, excel: Path, flit: Path) -> Path:
    carpeta = tmp_path / "salida"
    ejecutar(excel, flit, carpeta)
    return carpeta


def _spec(salida: Path) -> dict:
    return yaml.safe_load((salida / mapeo.ARCHIVO_SPEC).read_text(encoding="utf-8"))


def test_un_solo_openapi_31_con_una_url_base_por_ambiente(salida: Path) -> None:
    spec = _spec(salida)

    assert sorted(a.name for a in salida.iterdir()) == [
        mapeo.ARCHIVO_SPEC,
        "informe.md",
        "propuesta-flit",
        "trazabilidad.json",
    ]
    assert spec["openapi"] == "3.1.0"
    assert spec["info"]["title"] == "FLIT Core ICT API"
    assert spec["info"]["version"] == "1.1.0"
    assert spec["info"]["x-flit-visibilidad"] == "compartible"
    servidores = {s["x-flit-ambiente"]: s for s in spec["servers"]}
    assert set(servidores) == {"qa", "pdn"}
    assert all(s["url"] == "https://{host}" for s in servidores.values())
    assert servidores["qa"]["variables"]["host"]["default"] == "ict.qa.flitsas.com"
    assert servidores["pdn"]["variables"]["host"]["default"] == "ict.flitsas.com"
    assert spec["info"]["x-flit-ambientes"] == {"qa": "ict.qa.flitsas.com", "pdn": "ict.flitsas.com"}


def test_enriquece_el_login_con_descripciones_longitudes_ejemplos_y_codigos(salida: Path) -> None:
    spec = _spec(salida)
    login = spec["paths"]["/api/v1/auth/login"]["post"]
    peticion = spec["components"]["schemas"]["IctLoginRequest"]

    assert login["operationId"] == "postAuthLogin"
    assert login["x-public"] is True
    assert login["x-flit-sin-autenticacion"] is True
    assert peticion["properties"]["username"]["description"] == "Correo del gestor"
    assert peticion["properties"]["username"]["maxLength"] == 100
    assert login["requestBody"]["content"]["application/json"]["example"]["password"] == "$CLAVE"
    assert login["responses"]["200"]["content"]["application/json"]["example"] == {"token": "$TOKEN"}
    # Corrección verificada en el código de core-ict y códigos: el 404 del Excel (v1) no se inventa.
    respuesta = spec["components"]["schemas"]["IctLoginResponse"]["properties"]
    assert "expiresInSeconds" in respuesta and "expiresAt" not in respuesta
    assert {"200", "401", "403", "500"} <= set(login["responses"])
    assert "404" not in login["responses"]


def test_agrega_las_operaciones_v1_que_core_ict_implementa_y_su_contrato_no_declara(salida: Path) -> None:
    paths = _spec(salida)["paths"]

    for metodo, ruta in mapeo.OPERACIONES_DEL_CODIGO:
        op = paths[ruta][metodo]
        assert op["x-flit-origen"].startswith("código de core-ict")
        assert op["security"] == [{"ictClientToken": []}]
        assert op["x-public"] is True


def test_visibilidad_segun_el_token(salida: Path) -> None:
    paths = _spec(salida)["paths"]

    assert paths["/api/v1/ict/logs"]["get"]["x-internal"] is True
    assert "x-public" not in paths["/api/v1/ict/logs"]["get"]
    assert paths["/api/v1/status-process/byId/{id}"]["get"]["x-public"] is True
    # El parámetro compartido del contrato queda en línea y con la descripción del Excel... o la del contrato.
    parametro = paths["/api/v1/status-process/byId/{id}"]["get"]["parameters"][0]
    assert parametro["name"] == "id" and parametro["in"] == "path"


def test_estado_v1_usa_los_campos_del_codigo_de_core_ict(salida: Path) -> None:
    items = _spec(salida)["components"]["schemas"]["StatusProcessV1Response"]["properties"]["statusProcess"]["items"]

    assert "statusDescription" in items["properties"]
    assert "userMailRegistered" in items["properties"]


def test_tablas_de_referencia_como_enum_con_descripciones(salida: Path) -> None:
    componentes = _spec(salida)["components"]["schemas"]

    assert componentes["TipoTramite"]["enum"] == [1, 3, 5]
    assert componentes["TipoTramite"]["x-enum-descriptions"] == ["Matricula Inicial", "Traspaso Bilateral", "Blindaje"]
    assert componentes["TipoDocumento"]["enum"] == ["NIT", "CC"]
    vendedor = componentes["SolicitudRegistroTramite"]["properties"]["seller"]["items"]["properties"]
    assert vendedor["document_type"]["$ref"] == "#/components/schemas/TipoDocumento"


def test_obligatoriedad_por_tipo_de_tramite_con_oneof_y_extension(salida: Path) -> None:
    spec = _spec(salida)
    componentes = spec["components"]["schemas"]
    cuerpo = spec["paths"]["/api/v1/external-transaction/register"]["post"]["requestBody"]["content"][
        "application/json"
    ]

    # El contrato declaraba `{ transactions: [...] }`; core-ict lee un arreglo de filas.
    assert cuerpo["schema"]["type"] == "array"
    assert "RegisterBatchRequest" not in componentes
    variantes = [v["$ref"].rsplit("/", 1)[-1] for v in cuerpo["schema"]["items"]["oneOf"]]
    assert variantes == ["TramiteTraspasoBilateral", "TramiteMatriculaInicial", "TramiteOtro"]
    base = componentes["SolicitudRegistroTramite"]
    assert set(base["required"]) >= {"transaction_type", "seller"}
    bilateral = componentes["TramiteTraspasoBilateral"]["allOf"][1]
    assert bilateral["properties"]["transaction_type"]["const"] == 3
    assert {"plate", "buyer"} <= set(bilateral["required"])
    inicial = componentes["TramiteMatriculaInicial"]["allOf"][1]
    assert "vin" in inicial["required"]
    assert componentes["TramiteOtro"]["allOf"][1]["properties"]["transaction_type"]["enum"] == [5]
    fila_vin = next(f for f in base["x-flit-obligatoriedad"]["campos"] if f["campo"] == "vin")
    assert fila_vin == {
        "campo": "vin",
        "traspaso_bilateral": "opcional",
        "matricula_inicial": "obligatorio",
        "condicion": "Es obligatorio si es Matricula",
    }


def test_campo_que_core_ict_no_lee_queda_obsoleto(salida: Path) -> None:
    base = _spec(salida)["components"]["schemas"]["SolicitudRegistroTramite"]

    assert base["properties"]["company_manager_id"]["deprecated"] is True
    assert "company_manager_id" not in base.get("required", [])


def test_webhook_en_la_seccion_de_openapi_31_con_el_esquema_del_codigo(salida: Path) -> None:
    spec = _spec(salida)
    webhook = spec["webhooks"]["estadoTramite"]["post"]
    esquema = spec["components"]["schemas"]["NotificacionEstadoTramite"]

    assert webhook["x-public"] is True
    assert webhook["security"] == []  # core-ict no envía Authorization
    assert esquema["properties"]["transactionFlit"]["type"] == "string"  # manda el código, no el Excel
    assert esquema["properties"]["statusMessage"]["maxLength"] == 250
    assert "ictEstado" in esquema["properties"]
    assert webhook["requestBody"]["content"]["application/json"]["example"]["statusMessage"] == "Ver documento.pdf"


def test_changelog_inicial_con_colaborador_generico(salida: Path) -> None:
    changelog = _spec(salida)["info"]["x-flit-changelog"]

    assert changelog[0] == {
        "version": "1.2.0",
        "fecha": "2025-05-16",
        "hoja": "1 - Auth",
        "colaborador": "Equipo de integraciones de FLIT",
        "cambio": "Versión 1.2 de la hoja «1 - Auth» del Excel de contratos ICT.",
    }
    assert changelog[-1]["version"] == "1.1.0"
    assert all(e["colaborador"] == "Equipo de integraciones de FLIT" for e in changelog)


def test_cada_campo_del_excel_esta_en_el_spec_o_en_el_informe(salida: Path, excel: Path) -> None:
    libro = leer_libro(excel)
    traza = json.loads((salida / "trazabilidad.json").read_text(encoding="utf-8"))["campos"]
    informe = (salida / "informe.md").read_text(encoding="utf-8")
    por_celda = {(t["hoja"], t["celda"]): t for t in traza}

    campos = [c for h in libro.hojas for c in h.campos]
    assert campos
    for campo in campos:
        entrada = por_celda[(campo.hoja, campo.celda)]
        if entrada["destino"] == "spec":
            archivo, puntero = entrada["detalle"].split("#", 1)
            assert archivo == mapeo.ARCHIVO_SPEC
            resolver(yaml.safe_load((salida / archivo).read_text(encoding="utf-8")), puntero)
        else:
            assert f"`{campo.nombre}`" in informe


def test_ninguna_credencial_correo_ruta_local_ni_ip_privada_sobrevive(salida: Path) -> None:
    for archivo in salida.rglob("*"):
        if not archivo.is_file():
            continue
        texto = archivo.read_text(encoding="utf-8")
        assert buscar_sensibles(texto) == [], archivo.name
        for sensible in SENSIBLES:
            assert sensible not in texto, archivo.name


def test_informe_lista_las_inconsistencias_esperadas(salida: Path) -> None:
    informe = (salida / "informe.md").read_text(encoding="utf-8")

    assert "Hosts mezclados y URL vacías del Excel: **resuelto**" in informe
    assert "`ict.qa.flitsas.online` en «1 - Auth», «2 - Transacction»: resuelto" in informe
    assert "«RequestSign» está en el índice" in informe
    assert "`Transacction` en nombre de hoja" in informe
    assert "`GET /api/v1/secretaries` está en" in informe
    assert "el contrato no declara `operationId`" in informe
    assert "`ict.dev.flitsas.com` del Gateway no se publica" in informe
    assert "la hoja «Hoja2» está vacía" in informe
    assert "| Tokens con forma de JWT | 1 |" in informe


def test_propuesta_para_el_repo_flit(salida: Path) -> None:
    propuesta = (salida / mapeo.ARCHIVO_PROPUESTA).read_text(encoding="utf-8")
    leeme = (salida / "propuesta-flit/README.md").read_text(encoding="utf-8")

    assert propuesta.startswith("# Propuesta de flit-atlas")
    assert yaml.safe_load(propuesta) == _spec(salida)
    assert "## OpenAPI 3.1 y no 3.0.3" in leeme


def test_es_reproducible(tmp_path: Path, excel: Path, flit: Path) -> None:
    primera = ejecutar(excel, flit, tmp_path / "a")
    segunda = ejecutar(excel, flit, tmp_path / "b")

    assert primera == segunda


def test_sin_excel_publica_el_contrato_con_la_url_base_unica(tmp_path: Path, flit: Path) -> None:
    archivos = ejecutar(None, flit, tmp_path / "solo")

    spec = yaml.safe_load(archivos[mapeo.ARCHIVO_SPEC])
    assert spec["openapi"] == "3.1.0"
    assert {s["x-flit-ambiente"] for s in spec["servers"]} == {"qa", "pdn"}
    assert COLABORADOR not in archivos["informe.md"]


def test_cli(tmp_path: Path, excel: Path, flit: Path, capsys: pytest.CaptureFixture[str]) -> None:
    assert main(["--excel", str(excel), "--base-flit", str(flit), "--salida", str(tmp_path / "cli")]) == 0
    assert "archivos escritos" in capsys.readouterr().out
    assert (tmp_path / "cli" / mapeo.ARCHIVO_SPEC).is_file()
    # Las rutas locales de los argumentos no llegan a ningún archivo.
    for archivo in (a for a in (tmp_path / "cli").rglob("*") if a.is_file()):
        texto = archivo.read_text(encoding="utf-8")
        assert str(tmp_path) not in texto
        assert tmp_path.as_posix() not in texto


def test_sin_contrato_de_core_ict_falla(tmp_path: Path, excel: Path) -> None:
    with pytest.raises(SystemExit, match="core-ict.v1.yaml"):
        ejecutar(excel, tmp_path / "vacio", tmp_path / "salida")


def _cambiar_login(flit: Path, descripcion: str) -> None:
    contrato = flit / mapeo.CONTRATO_BASE
    documento = yaml.safe_load(contrato.read_text(encoding="utf-8"))
    documento["paths"]["/api/v1/auth/login"]["post"]["description"] = descripcion
    contrato.write_text(yaml.safe_dump(documento, allow_unicode=True, sort_keys=False), encoding="utf-8")


def test_los_textos_de_cara_al_cliente_salen_en_lenguaje_funcional(salida: Path) -> None:
    spec = _spec(salida)

    assert piezas_internas(spec) == []
    assert spec["info"]["description"] == mapeo.DESCRIPCION_INFO
    # La procedencia queda en extensiones que la poda quita, no en la descripción.
    fuente = spec["info"]["x-flit-fuente"]
    assert fuente["descripcion_contrato"].startswith("Contrato de prueba de core-ict")
    assert fuente["generador"].startswith("python -m atlas_docs.ict")
    webhook = spec["webhooks"]["estadoTramite"]["post"]
    assert "WebhookNotificationJob" in webhook["x-flit-origen"]
    assert webhook["x-flit-hoja-excel"] == "4 - WebHookTransaction"
    assert "core-ict" not in webhook["description"]


def test_un_texto_del_contrato_reescrito_guarda_el_original_en_una_extension(
    tmp_path: Path, excel: Path, flit: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    interno = "Autentica contra services/core-ict (`IctAuthEndpoints.LoginAsync`, Bug #1234)."
    _cambiar_login(flit, interno)
    puntero = "/paths/~1api~1v1~1auth~1login/post/description"
    monkeypatch.setitem(mapeo.TEXTOS_FUNCIONALES, puntero, ("Autentica al cliente ICT.", "nombra piezas internas"))

    ejecutar(excel, flit, tmp_path / "salida")

    login = _spec(tmp_path / "salida")["paths"]["/api/v1/auth/login"]["post"]
    assert login["description"].endswith("Autentica al cliente ICT.")
    assert login["x-flit-descripcion-contrato"] == interno
    informe = (tmp_path / "salida" / "informe.md").read_text(encoding="utf-8")
    assert "Textos del contrato reescritos en lenguaje funcional" in informe


def test_un_texto_con_piezas_internas_sin_reescribir_hace_fallar_la_generacion(
    tmp_path: Path, excel: Path, flit: Path
) -> None:
    _cambiar_login(flit, "Autentica contra el Gateway YARP (Feature #10888).")

    with pytest.raises(ValueError, match="piezas internas"):
        ejecutar(excel, flit, tmp_path / "salida")
    assert not (tmp_path / "salida").exists()


@pytest.mark.parametrize("valor", [TOKEN_FICTICIO, CORREO_AJENO, RUTA_LOCAL, IP_PRIVADA])
def test_los_valores_ficticios_de_prueba_se_detectan(valor: str) -> None:
    assert buscar_sensibles(f"texto {valor} texto")
