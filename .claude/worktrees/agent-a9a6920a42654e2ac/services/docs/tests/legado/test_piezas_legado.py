"""Piezas de la conversión del legado ICT: tipos del Excel, lectura tolerante de ejemplos y saneamiento."""

from collections import Counter
from pathlib import Path

import pytest
from conftest import CORREO_AJENO, IP_PRIVADA, RUTA_LOCAL, TOKEN_FICTICIO

from atlas_docs.legado.ejemplos import interpretar
from atlas_docs.legado.esquemas import Esquemas, romper_ciclos
from atlas_docs.legado.excel import leer_libro
from atlas_docs.legado.saneamiento import buscar_sensibles, sanear_ejemplo, sanear_texto
from atlas_docs.legado.tipos import esquema_desde_tipo


@pytest.mark.parametrize(
    ("texto", "esperado"),
    [
        ("String(12)", {"type": "string", "maxLength": 12}),
        ("String", {"type": "string"}),
        ("Min String(10)\nMax String(50)", {"type": "string", "minLength": 10, "maxLength": 50}),
        ('"Min String(1)\nMax String(50)"', {"type": "string", "minLength": 1, "maxLength": 50}),
        ("number(2)", {"type": "integer", "minimum": 0, "maximum": 99}),
        ("Number(1)", {"type": "integer", "minimum": 0, "maximum": 9}),
        ("number(17,2)", {"type": "number", "maximum": 999999999999999.99, "x-flit-decimales": 2}),
        ("number", {"type": "number"}),
        ("Integer", {"type": "integer"}),
        ("Boolean", {"type": "boolean"}),
        ("base64", {"type": "string", "contentEncoding": "base64"}),
        ("List", {"type": "array"}),
        ("N/A", None),
        ("algo raro", None),
    ],
)
def test_tipos_del_excel(texto: str, esperado: dict | None) -> None:
    assert esquema_desde_tipo(texto) == esperado


def test_ejemplo_con_comillas_tipograficas_comas_sobrantes_y_faltantes() -> None:
    ejemplo = interpretar('{\n  "a": “1”,\n  "b": 2\n  "c": [1, 2,],\n}')

    assert ejemplo is not None and ejemplo.tipo == "json"
    assert ejemplo.valores == [{"a": "1", "b": 2, "c": [1, 2]}]
    assert len(ejemplo.reparaciones) == 3


def test_varios_documentos_y_claves_con_espacios() -> None:
    ejemplo = interpretar('{"password ": "x"}\n\n{"message": "y"}')

    assert ejemplo is not None
    assert ejemplo.valores == [{"password": "x"}, {"message": "y"}]
    assert any("espacios" in r for r in ejemplo.reparaciones)


def test_curl_y_urls() -> None:
    curl = interpretar(
        "curl --request POST \\\n  --url https://api.ejemplo.com/x \\\n  --form id=3 \\\n  --form 'f=@a.pdf'"
    )
    urls = interpretar("\nhttps://api.ejemplo.com/a/1\nMOCK  http://api.ejemplo.com/a/1")

    assert curl is not None and curl.valores[0]["formulario"] == {"id": "3", "f": "@a.pdf"}
    assert urls is not None and urls.tipo == "urls" and len(urls.valores) == 2
    assert interpretar("{ roto") is not None and interpretar("{ roto").error  # type: ignore[union-attr]
    assert interpretar(None) is None


def test_sanear_texto_reemplaza_y_cuenta_sin_guardar_valores() -> None:
    cuenta: Counter = Counter()
    texto = (
        f"Authorization: Bearer {TOKEN_FICTICIO} {CORREO_AJENO} file=@{RUTA_LOCAL} http://{IP_PRIVADA}:8080/x "
        "https://abc123.execute-api.us-east-1.amazonaws.com/qa/x /home/persona/datos.txt"
    )

    limpio = sanear_texto(texto, cuenta)

    assert buscar_sensibles(limpio) == []
    assert "Bearer $TOKEN" in limpio
    assert "usuario@ejemplo.com" in limpio
    assert "documento.pdf" in limpio and "documento.txt" in limpio
    assert cuenta["host_interno"] == 2 and cuenta["correo"] == 1
    assert TOKEN_FICTICIO not in str(cuenta)


def test_sanear_ejemplo_por_nombre_de_clave() -> None:
    cuenta: Counter = Counter()
    ejemplo = {
        "manager_user": "Persona Real",
        "seller": [{"first_last_name": "Apellido", "phone": "3109998877", "email": CORREO_AJENO}],
        "password": "secreta",
        "token": TOKEN_FICTICIO,
        "filename": "factura.pdf",
        "companyregistered": "Empresa Real S.A.S",
        "priority": True,
    }

    limpio = sanear_ejemplo(ejemplo, cuenta)

    assert limpio == {
        "manager_user": "Nombre Demo",
        "seller": [{"first_last_name": "Apellido Demo", "phone": "3001234567", "email": "usuario@ejemplo.com"}],
        "password": "$CLAVE",
        "token": "$TOKEN",
        "filename": "factura.pdf",
        "companyregistered": "Organismo Demo",
        "priority": True,
    }


@pytest.mark.parametrize(
    "texto",
    [
        "usuario@ejemplo.com",
        "scheme: bearer\n  bearerFormat: JWT",
        "Bearer $TOKEN",
        "Bearer access_token",
        "192.0.2.10",
    ],
)
def test_lo_ficticio_no_se_marca(texto: str) -> None:
    assert buscar_sensibles(texto) == []


def test_ajustar_ejemplo_al_esquema() -> None:
    esquemas = Esquemas({"components": {"schemas": {}}})
    esquema = {
        "type": "object",
        "properties": {
            "a": {"type": "integer"},
            "b": {"type": "string", "maxLength": 3},
            "c": {"type": "boolean"},
        },
        "required": ["a", "c"],
        "additionalProperties": False,
    }
    problemas: list[str] = []

    valor = esquemas.ajustar({"a": "7", "b": "largo", "x": 1}, esquema, "$", problemas)

    assert valor == {"a": 7, "b": "lar", "c": True}
    assert len(problemas) == 4


def test_romper_ciclos_de_obligatorios() -> None:
    spec = {
        "components": {
            "schemas": {
                "A": {"type": "object", "properties": {"b": {"$ref": "#/components/schemas/B"}}, "required": ["b"]},
                "B": {
                    "type": "object",
                    "properties": {"a": {"type": "array", "items": {"$ref": "#/components/schemas/A"}}},
                    "required": ["a"],
                },
            }
        }
    }

    assert romper_ciclos(spec) == ["B.a"]
    assert "required" not in spec["components"]["schemas"]["B"]


def test_lectura_del_excel(excel: Path) -> None:
    libro = leer_libro(excel)
    auth = libro.hoja("Auth")
    tramite = libro.hoja("Transacction")

    assert libro.indice is not None and libro.indice.version == "1.0"
    assert libro.hojas_vacias == ["Hoja2"]
    assert auth is not None and auth.version == "1.2" and auth.urls["qa"] and not auth.urls["dev"]
    assert [c.codigo for c in auth.codigos] == [401, 404, 500]
    assert len(auth.ejemplos_salida) == 2
    assert tramite is not None and tramite.listado
    assert [t.titulo for t in tramite.tablas] == ["Tabla Tipo Documento", "Tabla Tipo Tramite"]
    vin = next(c for c in tramite.campos if c.nombre == "vin")
    assert vin.por_tipo == {"TRASPASO BILATERAL": "N", "MATRICULA INICIAL": "S"}
    assert vin.observacion == "Es obligatorio si es Matricula"
    assert all(not h.celdas_sin_interpretar for h in libro.hojas)
