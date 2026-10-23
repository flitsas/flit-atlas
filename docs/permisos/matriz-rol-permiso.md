# Matriz rol × permiso

> **Generado desde el código** (`CatalogoPermisos.MatrizEnMarkdown`, ATLAS-ADR-0023): no se edita a mano. Para regenerarlo: `ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests`. En Atlas se consulta en `GET /api/v1/plataforma/permisos/matriz`.

Todo empleado activo tiene además `atlas.lector`; `atlas.superadmin` recibe todos los permisos; `atlas.responsable` es implícito (responsable del aplicativo, solo con alcance a ese aplicativo, F2).

## Roles

| Código | Nombre | Tipo | Descripción |
|---|---|---|---|
| `atlas.superadmin` | Superadministrador | interno | Administración de Atlas: todo, incluida la configuración, los roles, los módulos y la auditoría completa. |
| `atlas.lider_tecnico` | Líder técnico | interno | Gestiona catálogo, responsables y rotación (aprueba cambios y compensaciones), paquetes, comparticiones e invitaciones; publica manualmente por excepción. |
| `atlas.desarrollador` | Desarrollador | interno | Ve todo lo interno_ti, propone cambios a documentos, participa en la rotación y solicita cambios de turno. |
| `atlas.soporte` | Soporte | interno | Mesa de ayuda e implementación: interno_negocio y notas de soporte; contratos internos y rotación en lectura. |
| `atlas.negocio` | Negocio | interno | Comercial interno, gerencia y capacitación: interno_negocio y fichas comerciales, sin detalle técnico ni infraestructura. |
| `atlas.gobierno_ia` | Gobierno de IA | interno | Comité y responsable de IA: módulo de gobierno de IA completo. |
| `atlas.auditor` | Auditor | interno | Seguridad y SGSI: lectura de auditoría y evidencias, sin edición. |
| `atlas.lector` | Lector | interno | Todo empleado: catálogo, responsables, calendario de soporte y documentación interno_negocio en lectura. |
| `ext.integrador` | Integrador | externo | Equipo técnico de un cliente que consume las APIs: paquetes de contratos compartidos, guías, changelog y avisos de deprecación. |
| `ext.cliente_funcional` | Cliente funcional | externo | Organismos de tránsito, CEA y gestoras: manuales y documentación funcional de los productos contratados. |
| `ext.asesor_comercial` | Asesor comercial | externo | Asesores comerciales externos: fichas comerciales y funcionales de los productos asignados. |
| `ext.proveedor` | Proveedor | externo | Consultores y proveedores: solo lo concedido explícitamente, con vencimiento obligatorio. |
| `ext.admin_organizacion` | Administrador de la organización | externo | Responsable del lado del cliente: invita y retira colegas de su propia organización dentro del alcance ya otorgado. |
| `atlas.responsable` | Responsable del aplicativo | implicito | Implícito: responsable principal o secundario; edita documentos, guías y diagramas y arma paquetes solo de su aplicativo. |

## Plataforma (`plataforma`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.plataforma.configurar` | Cambiar la configuración de Atlas y las banderas de los módulos. | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.roles.gestionar` | Asignar y retirar roles, con alcance y vigencia, y mapear grupos de Entra a roles. | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.roles.ver` | Ver los roles, las asignaciones de las personas, los mapeos de grupos y la matriz rol × permiso. | ✔ | ✔ |   |   |   |   | ✔ |   |   |   |   |   |   |   |
| `atlas.invitaciones.gestionar` | Invitar, reenviar, suspender, renovar y revocar invitaciones de externos (el administrador de una organización, solo de la suya). | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   | ✔ |   |
| `atlas.organizaciones.gestionar` | Crear y editar organizaciones (interna, clientes y proveedores). | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.auditoria.ver` | Leer la auditoría y las evidencias. | ✔ |   |   |   |   |   | ✔ |   |   |   |   |   |   |   |

## Catálogo (`catalogo`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.catalogo.ver` | Ver el catálogo de aplicativos y sus fichas. | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |
| `atlas.catalogo.gestionar` | Crear y editar productos, módulos y servicios del catálogo. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |

## Equipo y responsables (`equipo`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.equipo.ver` | Ver personas, responsables y el mapa de carga. | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |
| `atlas.equipo.gestionar` | Cambiar el reparto de responsables, traspasos y revisiones. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.equipo.ver_contacto` | Ver el teléfono y los datos de contacto de las personas. | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |

## Soporte de los sábados (`soporte`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.soporte.ver` | Ver el calendario de soporte de los sábados. | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |
| `atlas.soporte.solicitar_cambio` | Solicitar un cambio de turno y proponer reemplazo. | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.soporte.aprobar` | Aprobar cambios de turno y compensaciones. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.soporte.gestionar` | Configurar la rotación, generar e importar turnos. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |

## Contratos de integración (`contratos`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.contratos.ver_internos` | Ver los contratos internos (OpenAPI, AsyncAPI y proto) y su historial. | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |
| `atlas.contratos.publicar_manual` | Publicar una versión de contrato a mano, por excepción. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.contratos.guias.editar` | Editar las guías de integración. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   | ✔ |
| `atlas.paquetes.gestionar` | Armar paquetes de contratos y compartirlos con organizaciones. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   | ✔ |
| `atlas.portal.ver` | Entrar al portal de clientes y ver lo compartido con la organización. | ✔ | ✔ |   | ✔ |   |   |   |   | ✔ | ✔ | ✔ | ✔ | ✔ |   |
| `atlas.exportaciones.solicitar` | Solicitar exportaciones (PDF, Word, Postman, Excel, ZIP). | ✔ | ✔ | ✔ |   | ✔ |   |   |   |   |   | ✔ |   |   |   |
| `atlas.sandbox.usar` | Usar el sandbox de los contratos. | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |

## Documentación (`documentacion`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.docs.ver` | Ver documentos según su nivel de visibilidad. | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |   |
| `atlas.docs.editar` | Crear y editar borradores de documentos. | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   | ✔ |
| `atlas.docs.aprobar` | Aprobar y publicar documentos. | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   | ✔ |
| `atlas.docs.exportar` | Exportar documentos y colecciones. | ✔ | ✔ | ✔ | ✔ | ✔ |   |   |   |   |   | ✔ |   |   |   |

## Diagramas (`diagramas`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.arquitectura.ver` | Ver los diagramas de arquitectura (LikeC4) según su visibilidad. | ✔ | ✔ | ✔ |   |   |   |   |   |   |   | ✔ |   |   |   |
| `atlas.arquitectura.ver_despliegue` | Ver las vistas de despliegue por ambiente y el drift. | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   |   |
| `atlas.diagramas.editar` | Crear y editar diagramas del lienzo y funcionales. | ✔ | ✔ | ✔ |   |   |   |   |   |   |   |   |   |   | ✔ |

## Asistente Atlas (`asistente`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.asistente.usar` | Preguntar al asistente, limitado a lo que la persona puede ver. | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |   | ✔ | ✔ | ✔ | ✔ |   |   |
| `atlas.asistente.configurar` | Configurar el asistente, sus fuentes y sus topes. | ✔ |   |   |   |   | ✔ |   |   |   |   |   |   |   |   |

## Gobierno de IA (`gia`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.gia.ver` | Ver el inventario de casos de IA, políticas y costos. | ✔ |   |   |   |   | ✔ | ✔ |   |   |   |   |   |   |   |
| `atlas.gia.gestionar` | Gestionar casos de uso, riesgos, políticas y llaves virtuales. | ✔ |   |   |   |   | ✔ |   |   |   |   |   |   |   |   |
| `atlas.gia.auditar` | Revisar la evidencia de gobierno de IA. | ✔ |   |   |   |   | ✔ | ✔ |   |   |   |   |   |   |   |

## Identidad (`identidad`)

| Permiso | Descripción | `atlas.superadmin` | `atlas.lider_tecnico` | `atlas.desarrollador` | `atlas.soporte` | `atlas.negocio` | `atlas.gobierno_ia` | `atlas.auditor` | `atlas.lector` | `ext.integrador` | `ext.cliente_funcional` | `ext.asesor_comercial` | `ext.proveedor` | `ext.admin_organizacion` | `atlas.responsable` |
|---|---|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|:-:|
| `atlas.identidad.administrar` | Cerrar todas las sesiones de una persona, suspenderla y reactivarla. | ✔ |   |   |   |   |   |   |   |   |   |   |   |   |   |
