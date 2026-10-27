"""Lo común a todos los formatos: portada, marca de agua, índice, changelog, guías y contratos."""

from dataclasses import dataclass
from datetime import date

from atlas_docs.exportes.contenido import Contrato, ancla, construir
from atlas_docs.exportes.modelo import PaqueteExportar
from atlas_docs.exportes.specs import SpecLeido


def fecha_legible(valor: date) -> str:
    return valor.strftime("%d/%m/%Y")


@dataclass
class GuiaDoc:
    ancla: str
    titulo: str
    markdown: str


@dataclass
class EntradaDoc:
    revision: str
    fecha: str
    cambio: str


@dataclass
class Documento:
    paquete: str
    revision: int
    organizacion: str
    fecha: str  # dd/mm/aaaa
    dia: date
    hash: str
    changelog: list[EntradaDoc]
    guias: list[GuiaDoc]
    contratos: list[Contrato]

    @property
    def titulo(self) -> str:
        return f"{self.paquete} · revisión {self.revision}"

    @property
    def marca_agua(self) -> str:
        return f"{self.organizacion} · {self.fecha}"

    @property
    def leyenda(self) -> str:
        return f"Documento entregado a {self.organizacion} el {self.fecha}. Uso exclusivo de la organización."

    @property
    def nombre_base(self) -> str:
        return ancla(self.paquete, f"r{self.revision}")


def armar(paquete: PaqueteExportar, specs: list[SpecLeido], hash_contenido: str) -> Documento:
    m = paquete.metadatos
    guias: list[GuiaDoc] = []
    for i, g in enumerate(paquete.guias, start=1):
        guias.append(GuiaDoc(ancla(f"guia-{i}", g.titulo), g.titulo, g.markdown))
    return Documento(
        paquete=m.paquete,
        revision=m.revision,
        organizacion=m.organizacion,
        fecha=fecha_legible(m.fecha),
        dia=m.fecha,
        hash=hash_contenido,
        changelog=[EntradaDoc(e.revision, fecha_legible(e.fecha), e.cambio) for e in m.changelog],
        guias=guias,
        contratos=[construir(s) for s in specs],
    )
