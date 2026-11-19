# Insumos de Flit Atlas

| Archivo | En git | Uso |
|---|---|---|
| `Responsables_Productos_FLIT_2026-10-20.md` | Sí | Reparto de responsables, versión 5 (fase F2) |
| `Rotacion_Soporte_TI-Developers.xlsx` | Sí | Historial y programación de la rotación de los sábados (fase F2) |
| `Contratos_Integraciones_31Jul2025.xlsx` | **No** | Contratos de las APIs externas legadas ICT (fase F3) |

## Por qué el Excel de contratos no está en git

Según el prompt maestro (§8.4, punto 7), el Excel trae ejemplos con tokens con forma de JWT, correos de dominios de clientes y rutas locales de equipos personales. gitleaks no inspecciona el contenido de un `.xlsx`, así que versionarlo dejaría esos datos en el historial sin control. Está excluido en `.gitignore` y lo bloquea el pre-commit.

**Ubicación del original:** `[por verificar: ruta en SharePoint/OneDrive que defina el líder técnico]`.

En la fase F3 el importador trabaja sobre una copia local del archivo. Lo que llega a git es solo el OpenAPI 3.1 resultante, ya saneado con valores ficticios y variables (`$TOKEN`, `usuario@ejemplo.com`).
