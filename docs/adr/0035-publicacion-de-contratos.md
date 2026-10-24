---
id: ATLAS-ADR-0035
titulo: Publicación de contratos — token OIDC de GitHub Actions validado en Plataforma, lista blanca en el catálogo y versiones inmutables en el módulo de contratos
estado: propuesto
fecha: 2026-10-23
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro A-08, A-09, §7, §8.4 y §9, plan 00 D-05 y §8, HU 3.1, HU 3.2, ATLAS-ADR-0009, ATLAS-ADR-0010, ATLAS-ADR-0018, ATLAS-ADR-0023, ATLAS-ADR-0024, ATLAS-ADR-0026, ATLAS-ADR-0028]
---

# ATLAS-ADR-0035 · Publicación de contratos

## Contexto y problema

ATLAS-ADR-0009 decidió que la CI de cada repo publica en Atlas con el token OIDC de GitHub Actions y una lista blanca por repo, rama y workflow, para todas las fuentes (contratos en F3, documentación en F5, LikeC4 en F6). La HU 3.1 pide el validador del token y la lista blanca `catalogo_fuente_publicacion`, administrable con permiso y auditada (CA: repo o rama fuera de la lista → 403; token vencido → 401; pruebas con JWT firmados de prueba). La HU 3.2 pide `POST /publicaciones/contratos` con inmutabilidad (201, 200 idempotente, 409 con hash distinto, 422 con spec inválido) y el índice de operaciones y canales `contrato_operacion`, para OpenAPI, AsyncAPI y proto.

Hechos verificados en el código antes de decidir:

- Un módulo monta sus rutas en `/api/v1/<id>`, sus tablas llevan el prefijo `<id>_` y sus permisos `atlas.<id>.*` (`ExtensionesModulos.Validar` y `ArquitecturaModulosTests`, ATLAS-ADR-0026). Con el id `contratos`, la ruta del plan (`/publicaciones/contratos`) y el prefijo `contrato_` del plan 00 §8.4 no caben.
- El manifiesto «contratos» de `ManifiestosNucleo` traía, además de los tres `atlas.contratos.*`, cuatro permisos sin ese prefijo (`atlas.paquetes.gestionar`, `atlas.portal.ver`, `atlas.exportaciones.solicitar`, `atlas.sandbox.usar`).
- `RequiereScope` (ATLAS-ADR-0024) vive en `Flit.Atlas.Identidad`, que un módulo no puede referenciar; los Bearer ya se exceptúan de la antifalsificación (ATLAS-ADR-0008).
- No hay todavía abstracción de almacenamiento de objetos (S3) en el código; `services/render` (Go) solo tiene las sondas de salud: no valida contratos todavía (su diff llega en la HU 3.5).
- No hay eventos publicados aún en `atlas-events.v1.yaml`; el contexto de un módulo no puede mapear `plataforma_outbox` (solo mapea tablas con su prefijo).

## Factores de decisión

- Sin secretos en los repos y con certeza de qué repo, rama, workflow y commit publicó cada versión (ATLAS-ADR-0009).
- Un solo validador para todas las fuentes, sin que cada módulo lo repita.
- La lista blanca es configuración de seguridad: cambiarla exige permiso y queda auditada.
- Inmutabilidad real: ni la API ni un `UPDATE` a mano cambian una versión publicada.
- Nada de criptografía propia (A-03): la firma la verifica una librería de Microsoft.
- Respetar el contrato de módulo (ATLAS-ADR-0026) sin tocar los demás módulos más de lo necesario.

## Opciones consideradas

1. **Validador y esquema en Plataforma, lista blanca en el catálogo (lectura compartida `IFuentesPublicacion`), publicación y versiones en el módulo de contratos.**
2. Todo en el módulo de contratos (`contratos_fuente_publicacion` y su propio validador).
3. `JwtBearer` de ASP.NET Core con el descubrimiento OIDC de GitHub, en lugar de un manejador propio sobre `JsonWebTokenHandler`.
4. Validar los specs en atlas-render (Go, vacuum) en lugar de en .NET.

## Decisión

Opción elegida: **1**, con un manejador de autenticación propio sobre `JsonWebTokenHandler` (descartada la 3) y la validación de los specs en .NET (descartada la 4 por ahora).

### 1. Validador del token OIDC de GitHub Actions (Plataforma, HU 3.1)

- `Flit.Atlas.Plataforma.Publicacion`: esquema de autenticación `GitHubActions` (`Authorization: Bearer`), registrado por `AgregarPlataforma` para que lo usen contratos, documentación y arquitectura. Nunca abre la sesión del BFF y solo vale en los endpoints con `.RequierePublicacionCi()`: se autentican **solo** con ese esquema, así que la cookie de la PWA recibe 401 (como `RequiereScope`, ATLAS-ADR-0024).
- `ValidadorTokenGitHub`: firma RS256 con `JsonWebTokenHandler` (Microsoft.IdentityModel 8.23.0, la misma de Microsoft.Identity.Web), emisor `https://token.actions.githubusercontent.com`, audiencia del ambiente y vigencia con el reloj de Atlas (`TimeProvider`): `exp` y `nbf` con un margen de 1 minuto, `iat` no en el futuro y vida (`exp − iat`) de a lo sumo 1 hora. Un token de más de 16 KB, ilegible, sin firma (`alg: none`) o firmado con otra llave es 401. El motivo va al log; el token, nunca.
- **Audiencia:** `Plataforma__Publicacion__Audiencia`; si está vacía, `Atlas__UrlPublica` sin la barra final (`https://atlas.flitsas.com`, `https://qa.atlas.flitsas.com`…), que qa y pdn ya exigen. Así un token pedido para dev no sirve en pdn, y no hay configuración nueva obligatoria. Sin audiencia, ningún token vale.
- **Llaves (JWKS):** `LlavesGitHubActions` las pide a `<emisor>/.well-known/jwks` (cliente HTTP con nombre, tiempo máximo de 10 s), las guarda en memoria, las refresca cada 6 horas y cuando llega un `kid` desconocido (GitHub rotó), nunca más de una vez cada 30 segundos (un token inventado no hace que Atlas consulte a GitHub en cada petición). Si GitHub no responde, siguen las últimas llaves buenas. Solo se aceptan llaves RSA de firma con `kid`. No se usa `JwtBearer` porque su `ConfigurationManager` refresca en segundo plano y la petición que trae el `kid` nuevo fallaría; aquí el refresco es en la misma petición y con el reloj de Atlas.
- **Autorización global (403, antes de leer el cuerpo):** `repository_owner` en `Propietarios` (por defecto `flitsas`), `event_name` en `Eventos` (por defecto `push` y `workflow_dispatch`; nunca `pull_request`), `ref` de tipo rama (`refs/heads/*`, no tags) y `job_workflow_ref` en `WorkflowsReutilizables` (por defecto `flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v*`): el job debe correr el workflow reutilizable oficial en un tag, para que un repo no publique con un workflow propio (ATLAS-ADR-0009). Las listas vacías toman su valor por defecto (el enlazador de .NET suma en vez de reemplazar).
- `PublicadorCi` lleva lo que la lista blanca y la auditoría necesitan: repo (en minúsculas), dueño, rama, commit (`sha`), ruta del workflow que llamó (de `workflow_ref`), `job_workflow_ref`, evento, `run_id` y `run_attempt`; su firma es `github:dueño/repo@rama` (`publicado_por` del plan 00 §8.4).

### 2. Lista blanca en el catálogo (HU 3.1)

- Va en el **catálogo**, como dice el nombre del prompt y el plan 00 §7: cada fila es de un aplicativo (`aplicativo_id` con llave foránea y borrado en cascada: borrar el aplicativo revoca sus fuentes) y vale para todos los tipos de fuente (`openapi`, `asyncapi`, `proto`, `likec4`, `docs`), no solo contratos. En el módulo de contratos habría que moverla en F5 y F6.
- `catalogo_fuente_publicacion`: `repositorio` (`dueño/nombre` en minúsculas), `ramas` (exactas o con `*`: `release`, `hotfix/*`), `workflow` (ruta del workflow que llama, `.github/workflows/<nombre>.yml`, o vacío = cualquiera del repo; el reutilizable oficial se exige siempre), `tipos` y `nota`. Únicos por aplicativo, repo y workflow; la base revisa el formato del repo, que haya ramas y tipos válidos.
- Los demás módulos la leen con `IFuentesPublicacion` (Plataforma, lecturas compartidas de ATLAS-ADR-0031); sin catálogo, nadie publica. La comparación (`ListaBlancaPublicacion`) es una función pura: repo y workflow sin distinguir mayúsculas, ramas exactas o con comodín, aplicativo por slug y tipo exacto. Dice qué falta con un código estable: `fuente_no_autorizada` (repo, rama o workflow), `aplicativo_no_autorizado` o `tipo_no_autorizado`.
- Administración en `/api/v1/catalogo/fuentes-publicacion` (`GET`, `POST`, `PUT /{id}`, `DELETE /{id}`) con `atlas.catalogo.gestionar` (líder técnico) también para leer: es configuración de seguridad. Cada cambio queda en `plataforma_auditoria` (`catalogo.fuente_publicacion_creada | _actualizada | _eliminada`, con repo, ramas, workflow, tipos y los nombres de los campos cambiados) en la misma transacción. Revocar es eliminar la fila; lo ya publicado no cambia.
- **Lista blanca inicial** (`Semilla/fuentes-publicacion.json`, con la regla de la semilla del catálogo: una sola vez por clave `fuente:<aplicativo>:<repo>`, no revive lo eliminado): `atlas-api` ← `flitsas/flit-atlas`, ramas `develop`, `staging` y `release`, tipos `openapi` y `asyncapi`. Los repos de FLIT 2.0 y los `BackApiExternal*` se agregan cuando sus workflows existan (HU 3.10).

### 3. Módulo de contratos (HU 3.2)

- `Flit.Atlas.Modulos.Contratos`, id `contratos`, encendido por defecto (como su entrada en la PWA). Tablas **`contratos_*`** (el prefijo es el id del módulo, como `equipo_` y `soporte_`; el plan decía `contrato_*`): `contratos_contrato`, `contratos_version` y `contratos_operacion`, con su historial `__ef_migraciones_contratos`. Sin `organizacion_id` ni RLS: los contratos son internos; lo compartido con clientes irá por paquetes (F4). El aplicativo se guarda por id sin llave foránea (es de otro módulo) y se lee con `ILectorAplicativos`.
- Permisos: el módulo publica los tres `atlas.contratos.*` mudados de `ManifiestosNucleo.Contratos`, con los mismos códigos y concesiones. Los otros cuatro (paquetes, portal, exportaciones, sandbox) quedan en el núcleo en un manifiesto `portal` («Portal, paquetes y exportaciones», con el alcance `paquete`) hasta que F4 los mude a su módulo. Publicar desde la CI no es un permiso de persona: lo autorizan el token y la lista blanca.
- Ruta de publicación: **`POST /api/v1/contratos/publicaciones`** (el plan decía `/publicaciones/contratos`; las rutas de un módulo viven bajo `/api/v1/<id>`). Cuerpo **JSON** (no multipart): `aplicativo` (slug), `contrato` (código), `tipo`, `version` (opcional en OpenAPI y AsyncAPI), `spec` (texto, hasta 5 MB) y `changelog` (texto JSON de `oasdiff changelog`, hasta 1 MB). JSON se documenta y se valida igual que el resto del contrato y no necesita antifalsificación de formularios.
- Orden de las respuestas: 401 token inválido; 403 por la política (dueño, evento, workflow reutilizable) o por la lista blanca (repo, rama o workflow antes de leer el cuerpo; aplicativo y tipo después); 400 petición mal formada; 422 spec inválido (`spec_invalida`, con los errores) o versión que no cuadra (`version_invalida`, `version_distinta_del_spec`, `version_no_permitida_en_rama`); 409 `version_existente` (otro hash) o `tipo_distinto`; 200 idempotente; 201 con `Location`.
- **Versiones:** SemVer 2.0 estricto y sin metadatos de compilación (`+…`: irían en la URL y SemVer los considera la misma versión). En OpenAPI y AsyncAPI la versión es `info.version`, o `info.version-<prerelease>` si el workflow agrega el sufijo de la rama (plan 00 §8.2, regla 1). En proto es obligatoria y su MAJOR debe ser la del paquete (`flit.tramites.v1` → `1.x.y`). Por rama: `release` (`RamasEstables`) solo publica estables; `develop`, `X.Y.Z-dev.<algo>`; `staging`, `X.Y.Z-rc.<algo>`; otra rama de la lista blanca, cualquier prerelease. Un identificador de solo dígitos no puede empezar en cero (SemVer): con un sha7 como `0123456`, el workflow usa `dev.g<sha7>`.
- **Inmutabilidad:** el hash es SHA-256 de los bytes UTF-8 del spec tal como llegó. Misma versión y mismo hash → 200 sin cambios ni evento; otro hash → 409. Además, la base lo hace cumplir: un trigger de `contratos_version` rechaza cualquier `UPDATE` de contenido, hash, versión, procedencia o changelog (solo cambian `estado`, `retiro_en` y `actualizado_en`, para la deprecación), otro rechaza todo `UPDATE` de `contratos_operacion`, y la unicidad `(contrato, versión)` cubre dos publicaciones simultáneas (la segunda responde como si hubiera llegado después). Borrar (purgar) sigue siendo posible como decisión explícita, sin endpoint.
- El **spec se guarda en PostgreSQL** (`contenido`, `text` con TOAST), no en el almacenamiento de objetos del plan (`spec_objeto`): aún no hay S3 en el código y los specs de FLIT pesan cientos de KB. Moverlo a S3 es un cambio interno cuando llegue esa abstracción (F4, exportaciones) **[por verificar]**.
- El **changelog** viene de la CI (oasdiff, ATLAS-ADR-0010); `tiene_breaking` = algún cambio de nivel 3. El diff calculado en el servidor es la HU 3.5 (atlas-render).
- **Índice** `contratos_operacion` (clave única por versión, clase, método, ruta, `operationId`, resumen, etiquetas y `publica` por `x-public: true` o la etiqueta `public`): OpenAPI `GET /ruta` y webhooks `POST webhook:<nombre>`; AsyncAPI 2.x `publish|subscribe <canal>` y 3.x `send|receive <dirección del canal>` (con el id de la operación si se repite); proto `rpc:<paquete>.<Servicio>/<Método>`.
- La versión, su **auditoría** (`contratos.version_publicada`, actor `servicio` = `github:dueño/repo@rama`, con repo, rama, commit, workflow, `job_workflow_ref`, ejecución, intento y hash; nunca el token) y su **evento** `atlas.contratos.version_publicada` v1 en la outbox (ATLAS-ADR-0018, documentado en `atlas-events.v1.yaml`) van en una sola transacción: el contexto de la plataforma usa la conexión del módulo, como el catálogo con su auditoría.

### 4. Validación de los specs (HU 3.2)

- **OpenAPI 3.0 y 3.1** (JSON o YAML): `Microsoft.OpenApi` 2.12.2 (la misma que trae `Microsoft.AspNetCore.OpenApi` 10.0.13) con `Microsoft.OpenApi.YamlReader` 2.12.2 (MIT, con topes de tamaño, profundidad y alias). Son errores los del lector y los de su conjunto de reglas por defecto (campos obligatorios, referencias que no resuelven…). Swagger 2.0 es 422: se convierte antes (los `BackApiExternal*`, HU 3.8).
- **AsyncAPI 2.x y 3.x:** no hay una librería .NET madura sin dependencias problemáticas (los lectores de ByteBard dependen de `JsonPointer.Net`, cuya licencia de binarios cambió **[por verificar]**; Neuroglia arrastra `JsonSchema.Net` y su serialización). Se revisa la estructura que Atlas necesita (versión, `info.title` e `info.version`, canales, operaciones con `action` y `channel`) y que toda `$ref` local resuelva, sobre el YAML que ya lee SharpYaml. El lint completo es de vacuum en la CI (HU 3.3), como decide ATLAS-ADR-0010.
- **proto:** el parser de `protobuf-net.Reflection` 3.4.30 (Apache-2.0; trae los tipos conocidos de Google). Se publica un archivo a la vez: un `import` de otro archivo del repo y sus tipos son advertencias en la respuesta, no errores.
- Delegar en atlas-render queda para cuando tenga validación (A-09): hoy no la tiene y la publicación no debe depender de otro servicio.

### 5. Lectura interna y PWA

- `GET /api/v1/contratos` (consolidado con filtros por aplicativo, tipo y texto sin tildes; última versión y última estable), `GET /api/v1/contratos/{id}/versiones`, `GET /api/v1/contratos/{id}/versiones/{version}/spec` (tal cual, con `ETag` = SHA-256) y `GET /api/v1/contratos/{id}/versiones/{version}/operaciones`, todos con `atlas.contratos.ver_internos`. El proveedor de búsqueda `contratos` (tipos `contrato` y `operacion`, sobre la última versión) queda registrado para el endpoint global pendiente.
- El contrato OpenAPI declara el esquema `githubActions` (`openIdConnect` con el descubrimiento de GitHub) en la operación de publicación, sin cookie ni antifalsificación. `AutorizacionTests` reconoce `MetadatoPublicacionCi`: con la cookie de cualquier rol, 401.
- PWA: el módulo pasa de «próximamente» a una lista mínima de contratos (búsqueda y tipo en la URL) que al abrir uno pide sus versiones, y un widget con las últimas publicadas, todo en su chunk diferido. La pantalla completa del contrato es la HU 3.6.

### 6. Cómo llama la CI (para la HU 3.3)

```yaml
permissions:
  id-token: write
  contents: read
steps:
  - id: oidc
    uses: actions/github-script@<sha fijado>
    with:
      script: core.setOutput('token', await core.getIDToken(process.env.ATLAS_URL)) # audiencia = URL pública del ambiente
  - run: |
      jq -n --arg aplicativo "$APLICATIVO" --arg contrato "$CONTRATO" --arg tipo "$TIPO" --arg version "$VERSION" \
            --rawfile spec "$RUTA" --rawfile changelog changelog.json \
            '{aplicativo:$aplicativo, contrato:$contrato, tipo:$tipo, version:(if $version == "" then null else $version end), spec:$spec, changelog:$changelog}' > cuerpo.json
      curl -sS -o respuesta.json -w '%{http_code}' -X POST "$ATLAS_URL/api/v1/contratos/publicaciones" \
           -H "Authorization: Bearer ${{ steps.oidc.outputs.token }}" -H 'Content-Type: application/json' --data-binary @cuerpo.json
      # 201 o 200: bien; 409: subir la versión; 422: ver respuesta.json (errores); 401/403: token o lista blanca.
```

El job debe correr dentro del workflow reutilizable oficial en un tag `v*` (o lo que diga `WorkflowsReutilizables`), por `push` o `workflow_dispatch` a una rama de la lista blanca, y el repo, el aplicativo y el tipo deben tener su fila.

## Consecuencias

### Positivas

- Cero secretos: la identidad del publicador la da GitHub, y repo, rama, workflow, commit y ejecución quedan en cada versión y en la auditoría.
- Documentación (F5) y LikeC4 (F6) reutilizan el esquema, el requisito y la lista blanca con `.RequierePublicacionCi()` e `IFuentesPublicacion`.
- La inmutabilidad no depende solo de la API: la base la hace cumplir.
- Rotar llaves en GitHub no corta la publicación; GitHub caído tampoco, mientras haya llaves guardadas.

### Negativas

- El catálogo crece con una tabla y unos endpoints que usan otros módulos (mismo patrón de `ILectorAplicativos`).
- Los specs ocupan la base hasta que existan S3 y su purga.
- La validación de AsyncAPI es estructural: un documento que la pasa puede tener errores que solo vacuum ve (por eso el lint está en la CI).
- Mudar «contratos» dejó un manifiesto `portal` en el núcleo con cuatro permisos que F4 debe mudar; la matriz generada cambia de orden.
- El workflow reutilizable oficial vive en `flitsas/flit-atlas`, privado: los demás repos de la organización solo pueden llamarlo si el repo permite el acceso a sus workflows desde la organización **[por verificar con el líder en la HU 3.3]**.

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Todo en el módulo de contratos | La lista blanca es de aplicativos y de todos los tipos de fuente; habría que moverla o duplicarla en F5 y F6 |
| `JwtBearer` con el descubrimiento de GitHub | Su refresco de llaves es en segundo plano (la primera petición con el `kid` nuevo falla) y su vigencia usa el reloj del sistema, no `TimeProvider` |
| Validar los specs en atlas-render | Aún no valida nada (HU 3.5 hace el diff) y la publicación dependería de otro servicio; queda como mejora cuando lo tenga |
| Multipart como en el borrador del plan | Más difícil de documentar y validar en el contrato y obliga a desactivar la antifalsificación de formularios; JSON con `jq --rawfile` es igual de simple en la CI |
| Guardar el spec en S3 desde ya | No hay abstracción de almacenamiento todavía; meterla aquí agranda la HU sin necesidad |

## Más información

- [ATLAS-ADR-0009](0009-publicacion-ci-oidc-github.md) (OIDC de GitHub), [ATLAS-ADR-0010](0010-validacion-contratos-oasdiff-vacuum.md) (vacuum y oasdiff), [ATLAS-ADR-0018](0018-outbox-propio-y-broker-por-ambiente.md) (outbox), [ATLAS-ADR-0026](0026-registro-de-modulos-y-composicion.md) (módulos), [ATLAS-ADR-0028](0028-catalogo-de-aplicativos.md) (catálogo).
- Claims del token: <https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/about-security-hardening-with-openid-connect>.
- Pendientes **[por verificar]**: vida real de los tokens de GitHub (se acepta hasta 1 hora); validar también `repository_owner_id` (un nombre de organización puede cambiar de dueño); `runner_environment` si se usan runners propios; tag o sha fijados del workflow reutilizable (HU 3.3); acceso de los demás repos al workflow de un repo privado; mover los specs a S3; concesiones de los permisos de contratos.
