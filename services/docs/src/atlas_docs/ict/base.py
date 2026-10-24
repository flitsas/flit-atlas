"""Lectura de la base en el repo `flit` (FLIT 2): el contrato de core-ict y los hosts públicos del Gateway YARP.

Solo se leen dos archivos (`mapeo.CONTRATO_BASE` y `mapeo.GATEWAY`). De la ruta del repo no llega nada a la salida:
el spec y el informe citan las rutas relativas dentro de `flit`.
"""

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

import yaml

from atlas_docs.ict import mapeo


@dataclass
class Base:
    contrato: dict
    hosts: dict[str, str] = field(default_factory=dict)  # ambiente publicado → host público
    descartados: list[tuple[str, str]] = field(default_factory=list)  # (host, motivo) que no llegan al spec


def ambiente_de(host: str) -> str:
    """«dev», «qa» o «pdn» según el host (`ict.qa.flitsas.com` → qa; `ict.flitsas.com` → pdn)."""
    partes = host.split(".")
    for ambiente in ("dev", "qa"):
        if ambiente in partes[1:-2]:
            return ambiente
    return "pdn"


def hosts_del_gateway(gateway: dict) -> tuple[dict[str, str], list[tuple[str, str]]]:
    """Hosts de las rutas del Gateway que van al cluster de core-ict, separados en publicados y descartados."""
    rutas = gateway.get("ReverseProxy", {}).get("Routes", {})
    hosts: dict[str, str] = {}
    descartados: list[tuple[str, str]] = []
    for ruta in rutas.values():
        if ruta.get("ClusterId") != mapeo.CLUSTER_ICT:
            continue
        for host in ruta.get("Match", {}).get("Hosts", []) or []:
            host = host.lower()
            if not re.match(mapeo.HOSTS_PUBLICOS, host):
                # El nombre no se copia: puede ser local o interno.
                descartados.append(("host local o interno (omitido)", "no es un host público de FLIT"))
                continue
            ambiente = ambiente_de(host)
            if ambiente not in mapeo.AMBIENTES_PUBLICADOS:
                descartados.append((host, f"el ambiente {ambiente} es de uso interno de FLIT y no se publica"))
            elif ambiente in hosts:
                descartados.append((host, f"ya hay un host para {ambiente} (`{hosts[ambiente]}`)"))
            else:
                hosts[ambiente] = host
    return hosts, descartados


_JSONC = re.compile(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/', re.DOTALL)
_COMA_FINAL = re.compile(r",(\s*[}\]])")


def json_con_comentarios(texto: str) -> dict:
    """appsettings de .NET admite comentarios y comas finales; se quitan sin tocar el interior de los textos."""
    sin_comentarios = _JSONC.sub(lambda m: m.group(0) if m.group(0).startswith('"') else "", texto)
    return json.loads(_COMA_FINAL.sub(r"\1", sin_comentarios))


def leer_base(repo_flit: Path) -> Base:
    contrato_ruta = repo_flit / mapeo.CONTRATO_BASE
    if not contrato_ruta.is_file():
        raise SystemExit(f"no se encontró {mapeo.CONTRATO_BASE} en el repo flit indicado")
    contrato = yaml.safe_load(contrato_ruta.read_text(encoding="utf-8"))
    gateway_ruta = repo_flit / mapeo.GATEWAY
    hosts: dict[str, str] = {}
    descartados: list[tuple[str, str]] = []
    if gateway_ruta.is_file():
        hosts, descartados = hosts_del_gateway(json_con_comentarios(gateway_ruta.read_text(encoding="utf-8-sig")))
    return Base(contrato, hosts, descartados)
