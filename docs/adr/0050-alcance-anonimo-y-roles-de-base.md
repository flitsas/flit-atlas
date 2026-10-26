---
id: ATLAS-ADR-0050
titulo: Alcance de datos de las peticiones sin sesión y roles de la base — sin sesión no se ve nada salvo que el endpoint pida el alcance del sistema, y la aplicación usa un rol sin propiedad ni BYPASSRLS distinto del dueño (el migrador)
estado: aceptado (decisión del líder, 24/10/2026)
fecha: 2026-10-24
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §7, §9, §12 y §15, ATLAS-ADR-0023, ATLAS-ADR-0043, ATLAS-ADR-0044, ATLAS-ADR-0045, docs/seguridad/revision-f4.md (F4-SEC-10)]
---

# ATLAS-ADR-0050 · Alcance de datos sin sesión y roles de la base

## Contexto y problema

El modelo de permisos (§7, ATLAS-ADR-0023) se aplica en tres capas: política por endpoint, predicado obligatorio de EF
Core y RLS en las tablas con `organizacion_id` como defensa en profundidad. Las dos últimas leen el mismo
`AlcanceDatos` de la petición. La revisión de seguridad de F4 (hallazgo **F4-SEC-10**) encontró, el 24/10/2026:

- Sin sesión, `AlcanceDatosDeSesion` devolvía `AlcanceDatos.Sistema` (`atlas.alcance_total=on`), contra lo que decía
  `AlcanceDatos.Ninguno` («lo que ve una conexión sin variables»). Toda petición anónima —el proxy del sandbox, el feed
  `.ics`, el ingreso, los informes de CSP, el protocolo OAuth— veía todas las organizaciones en las dos capas: un error en
  uno de esos endpoints no lo frenaba RLS.
- `flit_atlas`, el rol de la aplicación, es el dueño del esquema y de las tablas (script de flit). Con `FORCE ROW LEVEL
  SECURITY` RLS le aplica, pero el dueño puede `ALTER TABLE … NO FORCE` o `DISABLE ROW LEVEL SECURITY`: una inyección SQL
  apaga la segunda barrera.
- Las pruebas de la API corrían como el superusuario del contenedor, que se salta RLS: nunca probaron la tercera capa con
  la aplicación de verdad.

## Factores de decisión

- Fallar cerrado: lo que no pide alcance no ve nada.
- Los procesos que sí necesitan ver todo (trabajos en segundo plano, migrador, ingreso antes de la sesión, OAuth, proxy
  del sandbox, feed) deben poder hacerlo sin trucos, y cada caso debe verse en el código y en la revisión del PR.
- No tocar las reglas de cada endpoint: la decisión debe vivir en un solo lugar y en la declaración del endpoint.
- La aplicación no debe poder cambiar el esquema ni la seguridad de las tablas.
- Nada que haga a la sonda de vida o a la generación del contrato depender de la base.

## Opciones consideradas

1. **Sin sesión, `Ninguno`; el alcance del sistema se pide explícitamente** (por metadata del endpoint o por un tramo de
   código), y dos roles de base. *Elegida.*
2. Sin sesión, `Ninguno`, y cada endpoint anónimo abre su propio contexto con alcance fijo. Más código por endpoint y fácil
   de olvidar en uno nuevo.
3. Mantener `Sistema` y confiar en la política por endpoint. Es lo que falló: RLS no sería defensa en profundidad.
4. Un solo rol con `NOBYPASSRLS` pero dueño. No impide `ALTER TABLE` ante una inyección.

## Decisión

### Alcance de datos (`AlcanceDatosDeSesion`, `ContextoAtlas`)

| Situación | Alcance |
|---|---|
| Sin petición HTTP (trabajos en segundo plano, consumidores de la cola, migrador, comandos) | `Sistema`: no hay a quién más atribuirlo |
| Petición con sesión de empleado | `Interno` (todas las organizaciones) |
| Petición con sesión de externo | su organización; sin organización, `Ninguno` |
| **Petición sin sesión** | **`Ninguno`** |
| Petición sin sesión a un endpoint con `ConAlcanceDelSistemaSinSesion()` | `Sistema` |
| Tramo con `using var _ = AlcanceDelSistema.Abrir();` | `Sistema` (manda sobre lo anterior) |

Piden el alcance del sistema, y por qué:

- **Grupo `/api/v1/identidad`** (`EndpointsIdentidad.MapIdentidad`): el ingreso local, las invitaciones, la recuperación y
  la emergencia buscan a la persona por su correo, enlace o credencial en cualquier organización, antes de tener sesión.
  Con sesión manda la de la persona.
- **`/connect/authorize`, `/connect/token`, `/connect/userinfo`** (`EndpointsTokens`): OpenIddict autentica al cliente y al
  titular del código o del refresh; el titular puede ser de cualquier organización.
- **Devolución de Entra** (`OnTokenValidated`, `ExtensionesIdentidad`): corre en el manejador remoto, sin endpoint, antes
  de crear la sesión; abre `AlcanceDelSistema` solo alrededor del aprovisionamiento.
- **Proxy del sandbox** (`EndpointsSandbox`, ATLAS-ADR-0045): busca la llave en los sandboxes de todas las organizaciones
  y luego revisa compartición, paquete y a quien lo activó.
- **Feed `.ics`** (`EndpointsM365`): revisa a la persona dueña del token, que es empleada (sin organización).

No lo piden (siguen con `Ninguno`): `/health/*`, `/openapi`, `/api/v1/version`, `POST /api/v1/plataforma/csp-reportes`
(solo escribe en el log). La publicación OIDC desde la CI (ATLAS-ADR-0035) se autentica con su propio esquema y escribe
contratos, que no tienen `organizacion_id`: no lo necesita. Agregar `ConAlcanceDelSistemaSinSesion()` a otro endpoint es
una decisión de seguridad que va en la revisión del PR, como agregarlo a `AutorizacionTests.Publicos`.

Correr las pruebas con el rol sin privilegios destapó un caso que ya fallaba en los ambientes (RLS aplica al dueño con
`FORCE`): en `TransaccionCompartida` el contexto de la identidad toma la conexión de la plataforma y, si es él quien la
vuelve a abrir, las variables de RLS no se fijaban (el de la identidad no tenía el interceptor) y la plataforma consultaba
sin alcance (la activación de la emergencia y la recuperación no abrían la sesión). Ahora `IdentidadDbContext` también
implementa `IContextoConAlcance` y lleva `InterceptorAlcanceDatos`.

### Roles de la base

| Rol | Quién lo usa | Propiedad | Atributos |
|---|---|---|---|
| `flit_atlas_migrador` | initContainer del migrador | dueño del esquema `atlas` y de sus tablas, secuencias y funciones | `LOGIN NOSUPERUSER NOBYPASSRLS` |
| `flit_atlas` | atlas-api | ninguna | `LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` |

El migrador concede a `flit_atlas`, al final de cada corrida y de forma idempotente (`RolesBaseDeDatos.Sql`): `USAGE` en
el esquema; `SELECT, INSERT, UPDATE, DELETE` en las tablas; solo `SELECT, INSERT` en `plataforma_auditoria` e
`identidad_evento_seguridad` (de solo inserción); nada en `__ef_migraciones_*`; `USAGE, SELECT` en las secuencias;
`EXECUTE` en las funciones (las de RLS); y los mismos privilegios por defecto para lo que creen las migraciones
siguientes. El nombre del rol de la aplicación sale de `Atlas__BaseDeDatos__RolAplicacion` (por defecto `flit_atlas`) y
se valida como identificador simple antes de ir al SQL. Los pasos del DBA (crear el rol del migrador, pasarle la
propiedad con `REASSIGN OWNED`, quitar privilegios a `flit_atlas`) están en `deploy/runbook-atlas.md` §3.

La sonda de preparación de atlas-api (`/health/ready`) incluye el chequeo `rol-base-de-datos`: en dev, qa y pdn falla si
el rol de la conexión es superusuario, tiene `BYPASSRLS` o es dueño de tablas de `atlas`; en local (el AppHost usa el
superusuario del contenedor) queda *degradado* con un aviso en el log. Va en la sonda y no en el arranque para que
`/health/live` y la generación del contrato no dependan de la base.

### Pruebas

Las pruebas de la API (`EntornoApi`) crean `flit_atlas` sin privilegios, migran con el superusuario (que hace de
migrador), conceden con `RolesBaseDeDatos` y corren atlas-api con `flit_atlas`: RLS aplica en todas. `RolesBaseDeDatosTests`
prueba que la aplicación no puede `ALTER TABLE … NO FORCE`, `DISABLE ROW LEVEL SECURITY`, `DROP POLICY`, cambiar la
auditoría, borrar el historial de migraciones ni crear tablas, y que sin variables no ve filas con organización.
`MatrizPermisosTests` y `PentestSesionesTests` prueban la tabla de alcances.

## Consecuencias

### Positivas

- RLS vuelve a ser segunda barrera también en los endpoints anónimos; un endpoint anónimo nuevo no ve nada hasta que lo
  pida explícitamente.
- Una inyección SQL en atlas-api no puede apagar RLS, cambiar el esquema ni borrar la auditoría.
- Las pruebas corren como en los ambientes.

### Negativas

- Un paso manual del DBA por ambiente antes de desplegar (runbook §3) y un SealedSecret nuevo para el migrador. Si se
  despliega sin hacerlo, atlas-api queda no lista fuera de local (falla cerrado; la versión anterior sigue sirviendo).
- Un endpoint anónimo que de verdad necesite datos de una organización y no declare el alcance falla (vacío o 404) en
  lugar de funcionar: se ve en las pruebas.
- El alcance del sistema en segundo plano sigue siendo implícito (por la falta de petición HTTP). Se aceptó: exigir un
  `AlcanceDelSistema.Abrir()` en cada trabajo no agrega control (no hay nadie más a quien atribuirlo) y sí cambios en
  módulos que no tienen datos de organizaciones.

## Opciones descartadas y por qué

- **Opción 2:** repite en cada endpoint lo que la metadata dice en una línea, y un contexto con alcance fijo es más fácil
  de copiar a donde no corresponde.
- **Opción 3:** es el estado que encontró la revisión.
- **Opción 4:** el dueño puede apagar RLS; no cierra el hallazgo.

## Más información

- [ATLAS-ADR-0023](0023-modelo-de-roles-y-permisos.md): predicado obligatorio y RLS.
- [ATLAS-ADR-0045](0045-sandbox-con-microcks.md): proxy del sandbox.
- `docs/seguridad/revision-f4.md`, hallazgo F4-SEC-10.
- **[por verificar]** que el script de flit `servicio-con-esquema-propio.sql` siga creando `flit_atlas` como dueño: si se
  cambia allí para crear los dos roles, los pasos 1 a 3 del runbook quedan solo para las bases ya creadas (pedirlo en
  flit, no desde este repo).
