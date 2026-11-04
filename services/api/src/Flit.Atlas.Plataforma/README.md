# Flit.Atlas.Plataforma

Servicios transversales: organizaciones, personas, roles y asignaciones con alcance y vigencia, evaluación de permisos, auditoría inmutable, notificaciones, búsqueda y banderas de módulo. Tablas `plataforma_*`. Fase **F1**.

Clientes de los servicios internos: `Render/IRenderDiff` pide a atlas-render el diff entre dos versiones de un contrato (HU 3.5, ATLAS-ADR-0036); `RenderDiffSimulado` lo reemplaza en las pruebas de los módulos.
