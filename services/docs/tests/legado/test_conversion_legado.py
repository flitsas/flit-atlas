"""Conversión de punta a punta con el swagger mínimo y el xlsx ficticio de conftest.py (HU 3.8)."""

import json
from pathlib import Path

import pytest
import yaml
from conftest import COLABORADOR, CORREO_AJENO, IP_PRIVADA, RUTA_LOCAL, SENSIBLES, TOKEN_FICTICIO

from atlas_docs.legado.__main__ import ejecutar, main
from atlas_docs.legado.excel import leer_libro
from atlas_docs.legado.saneamiento import buscar_sensibles


def resolver(documento: dict, puntero: str) -> object:
    nodo: object = documento
    for parte in puntero.lstrip("/").split("/"):
        parte = parte.replace("~1", "/").replace("~0", "~")
        nodo = nodo[int(parte)] if isinstance(nodo, list) else nodo[parte]  # type: ignore[index]
    return nodo


@pytest.fixture
def salida(tmp_path: Path, excel: Path, repos: Path) -> Path:
    carpeta = tmp_path / "salida"
    ejecutar(excel, repos, carpeta)
    return carpeta


def _spec(salida: Path, codigo: str) -> dict:
    return yaml.safe_load((salida / f"{codigo}.yaml").read_text(encoding="utf-8"))


def test_genera_openapi_31_con_version_titulo_y_servidor(salida: Path) -> None:
    spec = _spec(salida, "external-auth")

    assert spec["openapi"] == "3.1.0"
    assert spec["info"]["version"] == "1.2.0"
    assert "boilerplate" not in spec["info"]["title"]
    assert spec["info"]["x-flit-visibilidad"] == "compartible"
    assert spec["servers"][0]["variables"]["host"]["default"] == "ict.qa.flitsas.online"
    assert spec["info"]["x-flit-ambientes"]["dev"] == "por confirmar"


def test_enriquece_con_descripciones_longitudes_ejemplos_y_codigos(salida: Path) -> None:
    spec = _spec(salida, "external-auth")
    login = spec["paths"]["/api/v1/auth/login"]["post"]
    peticion = spec["components"]["schemas"]["LoginServiceRequestDto"]

    assert login["x-public"] is True
    assert login["x-flit-sin-autenticacion"] is True
    assert peticion["properties"]["username"]["description"] == "Correo del gestor"
    assert peticion["properties"]["username"]["maxLength"] == 100
    assert login["requestBody"]["content"]["application/json"]["example"]["password"] == "$CLAVE"
    assert login["responses"]["200"]["content"]["application/json"]["example"] == {"token": "$TOKEN"}
    assert {"401", "404", "500"} <= set(login["responses"])
    assert login["responses"]["401"]["description"] == "No autorizado."


def test_marca_internas_las_operaciones_que_el_excel_no_documenta(salida: Path) -> None:
    spec = _spec(salida, "external-auth")

    assert spec["paths"]["/api/v1/company-parameters"]["post"]["x-internal"] is True
    assert spec["paths"]["/api/v1/health"]["get"]["x-internal"] is True
    assert "headers" not in spec["paths"]["/api/v1/health"]["get"]["responses"]["200"]


def test_tablas_de_referencia_como_enum_con_descripciones(salida: Path) -> None:
    componentes = _spec(salida, "external-transact")["components"]["schemas"]

    assert componentes["TipoTramite"]["enum"] == [1, 3, 5]
    assert componentes["TipoTramite"]["x-enum-descriptions"] == ["Matricula Inicial", "Traspaso Bilateral", "Blindaje"]
    assert componentes["TipoDocumento"]["enum"] == ["NIT", "CC"]
    vendedor = componentes["SolicitudRegistroTramite"]["properties"]["seller"]["items"]["properties"]
    assert vendedor["document_type"]["$ref"] == "#/components/schemas/TipoDocumento"


def test_obligatoriedad_por_tipo_de_tramite_con_oneof_y_extension(salida: Path) -> None:
    spec = _spec(salida, "external-transact")
    componentes = spec["components"]["schemas"]
    cuerpo = spec["paths"]["/api/v1/external-transaction/register"]["post"]["requestBody"]["content"][
        "application/json"
    ]

    variantes = [v["$ref"].rsplit("/", 1)[-1] for v in cuerpo["schema"]["items"]["oneOf"]]
    assert variantes == ["TramiteTraspasoBilateral", "TramiteMatriculaInicial", "TramiteOtro"]
    base = componentes["SolicitudRegistroTramite"]
    assert set(base["required"]) >= {"company_manager_id", "transaction_type", "seller"}
    bilateral = componentes["TramiteTraspasoBilateral"]["allOf"][1]
    assert bilateral["properties"]["transaction_type"]["const"] == 3
    assert {"plate", "buyer"} <= set(bilateral["required"])
    inicial = componentes["TramiteMatriculaInicial"]["allOf"][1]
    assert "vin" in inicial["required"]
    assert componentes["TramiteOtro"]["allOf"][1]["properties"]["transaction_type"]["enum"] == [5]
    matriz = base["x-flit-obligatoriedad"]
    fila_vin = next(f for f in matriz["campos"] if f["campo"] == "vin")
    assert fila_vin == {
        "campo": "vin",
        "traspaso_bilateral": "opcional",
        "matricula_inicial": "obligatorio",
        "condicion": "Es obligatorio si es Matricula",
    }


def test_webhooks_en_la_seccion_de_openapi_31(salida: Path) -> None:
    spec = _spec(salida, "external-transactwebhook")
    webhook = spec["webhooks"]["estadoTramite"]["post"]

    assert webhook["x-public"] is True
    esquema = spec["components"]["schemas"]["NotificacionEstadoTramite"]
    assert esquema["properties"]["transactionFlit"]["type"] == "number"
    assert esquema["properties"]["statusMessage"]["maxLength"] == 250
    assert webhook["requestBody"]["content"]["application/json"]["example"]["statusMessage"] == "Ver documento.pdf"


def test_changelog_inicial_con_colaborador_generico(salida: Path) -> None:
    changelog = _spec(salida, "external-transact")["info"]["x-flit-changelog"]

    assert changelog == [
        {
            "version": "3.0.0",
            "fecha": "2025-05-16",
            "hoja": "2 - Transacction",
            "colaborador": "Equipo de integraciones de FLIT",
            "cambio": "Versión 3 de la hoja «2 - Transacction» del Excel de contratos ICT.",
        }
    ]


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
            resolver(yaml.safe_load((salida / archivo).read_text(encoding="utf-8")), puntero)
        else:
            assert f"`{campo.nombre}`" in informe


def test_ninguna_credencial_ni_ruta_local_sobrevive(salida: Path) -> None:
    for archivo in salida.rglob("*"):
        if not archivo.is_file():
            continue
        texto = archivo.read_text(encoding="utf-8")
        assert buscar_sensibles(texto) == [], archivo.name
        for sensible in SENSIBLES:
            assert sensible not in texto, archivo.name


def test_informe_lista_las_inconsistencias_esperadas(salida: Path) -> None:
    informe = (salida / "informe.md").read_text(encoding="utf-8")

    assert "«RequestSign» está en el índice" in informe
    assert "`Transacction` en nombre de hoja" in informe
    assert "sin URL de dev, pdn" in informe
    assert "`CreateNew` se repite" in informe
    assert "la hoja «Hoja2» está vacía" in informe
    assert "| Tokens con forma de JWT | 1 |" in informe


def test_propuestas_para_los_repos_legados(salida: Path) -> None:
    tsoa = json.loads((salida / "propuestas/BackApiExternalAuth/tsoa.json").read_text(encoding="utf-8"))
    workflow = (salida / "propuestas/BackApiExternalAuth/.github/workflows/atlas-publicar.yml").read_text(
        encoding="utf-8"
    )

    assert tsoa["spec"]["version"] == "1.2.0"
    assert "boilerplate" not in tsoa["spec"]["name"]
    assert tsoa["entryFile"] == "src/index.ts"
    assert "uses: flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@" in workflow
    assert "contrato: external-auth" in workflow
    assert yaml.safe_load(workflow)["jobs"]["publicar"]["permissions"]["id-token"] == "write"


def test_es_reproducible(tmp_path: Path, excel: Path, repos: Path) -> None:
    primera = ejecutar(excel, repos, tmp_path / "a")
    segunda = ejecutar(excel, repos, tmp_path / "b")

    assert primera == segunda


def test_sin_excel_convierte_solo_el_swagger(tmp_path: Path, repos: Path) -> None:
    archivos = ejecutar(None, repos, tmp_path / "solo")

    spec = yaml.safe_load(archivos["external-auth.yaml"])
    assert spec["info"]["x-flit-visibilidad"] == "interna"
    assert COLABORADOR not in archivos["informe.md"]


def test_cli(tmp_path: Path, excel: Path, repos: Path, capsys: pytest.CaptureFixture[str]) -> None:
    assert main(["--excel", str(excel), "--repos", str(repos), "--salida", str(tmp_path / "cli")]) == 0
    assert "archivos escritos" in capsys.readouterr().out
    assert (tmp_path / "cli" / "external-transact.yaml").is_file()
    # Las rutas locales de los argumentos no llegan a ningún archivo.
    for archivo in (a for a in (tmp_path / "cli").rglob("*") if a.is_file()):
        texto = archivo.read_text(encoding="utf-8")
        assert str(tmp_path) not in texto
        assert tmp_path.as_posix() not in texto


@pytest.mark.parametrize("valor", [TOKEN_FICTICIO, CORREO_AJENO, RUTA_LOCAL, IP_PRIVADA])
def test_los_valores_ficticios_de_prueba_se_detectan(valor: str) -> None:
    assert buscar_sensibles(f"texto {valor} texto")
