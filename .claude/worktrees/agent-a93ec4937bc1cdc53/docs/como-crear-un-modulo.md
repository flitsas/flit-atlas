# Cómo crear un módulo de Flit Atlas

Atlas crece por módulos (prompt maestro §2 y §10): cada capacidad es un módulo enchufable, detrás de su bandera, que se agrega **sin tocar los demás**. Esta guía lo hace paso a paso con el módulo de ejemplo **«hola»**, que ya está en el repo y sirve de plantilla:

- Backend: [`services/api/src/Flit.Atlas.Modulos.Hola/`](../services/api/src/Flit.Atlas.Modulos.Hola/) y sus pruebas en [`services/api/tests/Flit.Atlas.Modulos.Hola.Tests/`](../services/api/tests/Flit.Atlas.Modulos.Hola.Tests/).
- Frontend: [`apps/web/src/modulos/hola/`](../apps/web/src/modulos/hola/).

Decisiones detrás de esta guía: [ATLAS-ADR-0016](adr/0016-contrato-de-modulo.md) (contrato de módulo), [ATLAS-ADR-0023](adr/0023-modelo-de-roles-y-permisos.md) (permisos, predicado obligatorio y RLS), [ATLAS-ADR-0021](adr/0021-contratos-propios-openapi-y-asyncapi.md) (contratos), [ATLAS-ADR-0024](adr/0024-servidor-de-tokens-openiddict.md) (scopes) y [ATLAS-ADR-0026](adr/0026-registro-de-modulos-y-composicion.md) (registro y composición).

En los ejemplos, `<x>` es el id del módulo (snake_case, p. ej. `soporte`) y `<X>` su nombre en PascalCase (`Soporte`).

## Resumen: qué archivos tocas

| Dónde | Qué | ¿Es de otro módulo? |
|---|---|---|
| `services/api/src/Flit.Atlas.Modulos.<X>/` | Proyecto nuevo: módulo, permisos, contexto, migraciones y endpoints | No: es tuyo |
| `services/api/tests/Flit.Atlas.Modulos.<X>.Tests/` | Pruebas del módulo | No: es tuyo |
| `services/api/src/Flit.Atlas.Composicion/Flit.Atlas.Composicion.csproj` | Una `ProjectReference` | No: es la composición del host |
| `services/api/src/Flit.Atlas.Composicion/ModulosAtlas.cs` | Una línea en `Registrados` | No: es la composición del host |
| `Flit.Atlas.slnx` | Los dos proyectos | No |
| `services/api/Dockerfile` | Un `COPY` del `.csproj` (caché de la restauración) | No |
| `apps/web/src/modulos/<x>/` | Manifiesto `ModuloAtlas`, páginas y widgets | No: es tuyo |
| `apps/web/src/app/modulos/registro.ts` | Una línea en `MODULOS_REGISTRADOS` | No: es el registro del shell |
| `contracts/openapi/atlas.v1.yaml` y `docs/permisos/matriz-rol-permiso.md` | Se **regeneran**, no se editan | Generados |
| `contracts/asyncapi/atlas-events.v1.yaml` | Solo si publicas eventos | Contrato |
| `apps/web/src/app/modulos/contrato.ts` y `ui/Icono.tsx` | Solo si necesitas un ícono nuevo | Shell |

Nada más. Program.cs, el migrador, Identidad, Plataforma y los demás módulos no cambian: los descubre la composición.

## 1. Backend

### 1.1 Crea el proyecto

```bash
dotnet new classlib -o services/api/src/Flit.Atlas.Modulos.<X> --framework net10.0
rm services/api/src/Flit.Atlas.Modulos.<X>/Class1.cs
```

Deja el `.csproj` como el de `Flit.Atlas.Modulos.Hola.csproj`: `FrameworkReference` a `Microsoft.AspNetCore.App`, `Microsoft.EntityFrameworkCore.Design` (solo para las migraciones) y **una sola** referencia de proyecto, a `Flit.Atlas.Plataforma`. Un módulo nunca referencia a otro módulo ni a `Flit.Atlas.Identidad`: `ArquitecturaModulosTests` falla si lo haces. Para hablar con otro módulo, usa eventos (ATLAS-ADR-0018) o una interfaz de lectura publicada en Plataforma: `ILectorAplicativos` (la implementa el catálogo), `ISoporteDeTurno` (la implementa el soporte) e `IFuentesPublicacion` (la lista blanca de publicación desde la CI, del catálogo) en `Plataforma/Modulos/LecturasCompartidas.cs`, con una implementación vacía por defecto (ATLAS-ADR-0031 y ATLAS-ADR-0035). Si tu módulo recibe publicaciones de la CI de los repos (documentación, LikeC4), usa `.RequierePublicacionCi()` en el endpoint y `IFuentesPublicacion` con `ListaBlancaPublicacion.Autorizar`, como el módulo de contratos: el token OIDC de GitHub Actions ya lo valida Plataforma.

Las versiones de los paquetes viven en `Directory.Packages.props`; un paquete nuevo va ahí con versión exacta y al menos dos semanas de publicada.

### 1.2 Declara los permisos

Usa `atlas.<x>.<accion>` y di qué roles los reciben (los roles son del núcleo, `RolesAtlas`; un módulo no crea roles). Si tu módulo es uno de la lista base del §7.4 (equipo, soporte…), sus permisos ya existen en `ManifiestosNucleo`: **muda** ese manifiesto a tu módulo con los mismos códigos y concesiones (son contrato), agrégale los permisos nuevos y quítalo de `ManifiestosNucleo.Todos`: dos manifiestos con el mismo id no conviven. El catálogo ya lo hizo (`PermisosCatalogo`, [ATLAS-ADR-0028](adr/0028-catalogo-de-aplicativos.md)).

```csharp
public static class PermisosHola
{
    public const string Ver = "atlas.hola.ver";
    public const string Gestionar = "atlas.hola.gestionar";

    public static ManifiestoPermisos Manifiesto { get; } = new("hola", "Hola (ejemplo)",
    [
        new(Ver, "Ver los saludos de tu organización (módulo de ejemplo).", Lector, Integrador),
        new(Gestionar, "Escribir saludos en tu organización (módulo de ejemplo).", LiderTecnico, Desarrollador),
    ]);
}
```

`atlas.superadmin` recibe todo sin declararlo, y todo empleado tiene `atlas.lector`.

### 1.3 Implementa `IModuloAtlas`

```csharp
public sealed class ModuloHola : IModuloAtlas
{
    public const string IdModulo = "hola";
    public string Id => IdModulo;
    public string Nombre => "Hola";                       // etiqueta del contrato OpenAPI
    public string Descripcion => "…";                     // descripción de la etiqueta
    public bool ActivoPorDefecto => false;                // bandera al crearse en cada ambiente
    public ManifiestoPermisos Permisos => PermisosHola.Manifiesto;
    public Type? ContextoDatos => typeof(HolaDbContext);  // null si no tiene tablas

    public void RegistrarDatos(IServiceCollection servicios, string cadenaConexion) =>
        servicios.AddDbContext<HolaDbContext>(o => HolaDbContext.Configurar(o, cadenaConexion));

    public void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion) { }

    public void MapearEndpoints(RouteGroupBuilder grupo) => EndpointsHola.Mapear(grupo);
}
```

Opcionales (tienen valor por defecto vacío): `EventosPublicados` y `EventosConsumidos` (`TipoEvento` `atlas.<x>.<evento>`), `TiposNotificacion` (`<x>.<tipo>`) y `Scopes` (`ScopeModulo` `atlas.<x>` o `atlas.<x>.<accion>`, para los tokens de clientes que no son el navegador, ATLAS-ADR-0024). Un proveedor de búsqueda se registra en `RegistrarServicios` con `servicios.AgregarProveedorBusqueda<MiProveedor>()`. Los datos iniciales van en `SembrarAsync(IServiceProvider, CancellationToken)`: lo corre el migrador después de todas las migraciones, en cada despliegue, así que debe ser idempotente (mira la semilla del catálogo). Si algo no lleva el prefijo del módulo, atlas-api no arranca y te dice qué.

### 1.4 Datos: contexto, prefijo y RLS

- Un `DbContext` propio que deriva de `ContextoAtlas`, con tablas `<x>_*` en el esquema `atlas` y su historial `__ef_migraciones_<x>` (`ContextoAtlas.ConfigurarOpciones(opciones, cadena, ExtensionesModulos.TablaHistorial("<x>"))`). Mira `Datos/HolaDbContext.cs`.
- Entidades que derivan de `Entidad` (uuid v7, `creado_en` y `actualizado_en` en `timestamptz`).
- Toda tabla con datos de una organización lleva `OrganizacionId` y, en `OnModelCreating`, `AplicarPredicadoDeOrganizacion(modelBuilder)` (predicado obligatorio).
- Columnas con datos personales: `.HasComment(PlataformaDbContext.Pii)` (`@pii`).
- Una `IDesignTimeDbContextFactory` interna para `dotnet ef` (como `FabricaDeDiseno` en hola).

Crea la migración:

```bash
dotnet tool restore
dotnet ef migrations add Inicial -p services/api/src/Flit.Atlas.Modulos.<X> -o Datos/Migraciones
```

Y agrégale a mano, al final de `Up`, la llave foránea al núcleo y **RLS forzada** en cada tabla con `organizacion_id` (el núcleo se migra antes, así que la función ya existe):

```csharp
migrationBuilder.Sql("""
    ALTER TABLE atlas.hola_saludo ADD CONSTRAINT fk_hola_saludo_plataforma_organizacion_organizacion_id
        FOREIGN KEY (organizacion_id) REFERENCES atlas.plataforma_organizacion (id) ON DELETE RESTRICT;
    ALTER TABLE atlas.hola_saludo ENABLE ROW LEVEL SECURITY;
    ALTER TABLE atlas.hola_saludo FORCE ROW LEVEL SECURITY;
    CREATE POLICY organizacion_visible ON atlas.hola_saludo
        USING (atlas.plataforma_organizacion_visible(organizacion_id))
        WITH CHECK (atlas.plataforma_organizacion_visible(organizacion_id));
    """);
```

Sin `FORCE`, `flit_atlas` (dueño de las tablas) se salta RLS. `ArquitecturaModulosTests` falla si una tabla con `organizacion_id` queda sin RLS forzada o sin política.

### 1.5 Endpoints

El host ya montó tu grupo en `/api/v1/<x>` con la etiqueta del módulo, sesión obligatoria y la bandera (404 si está apagada para la organización de la persona). En cada endpoint:

- `.RequierePermiso(PermisosHola.Ver)`: sin permiso, 403. `AutorizacionTests` recorre todos los endpoints con todos los roles y falla si uno no declara permiso.
- `.WithName("VerboSustantivo")` (el `operationId`, único y en español), `.WithSummary(...)` y `.WithDescription(...)`.
- Resultados tipados (`Results<Ok<T>, NotFound>`, `Created<T>`, `ValidationProblem`) para que el contrato traiga todas las respuestas; `[Description]`, `[MinLength]` y `[MaxLength]` en los records.
- La organización y la persona salen de la sesión (`EndpointsPlataforma.ClaimOrganizacion` y `ClaimPersona`), nunca del cuerpo.
- Una acción sensible (§6.3: compartir, cambiar roles, exportar) lleva `.RequiereReautenticacion()` (`Flit.Atlas.Plataforma.Permisos`): sin un ingreso de menos de 10 minutos, 401 `reautenticacion_requerida`.

Mira `EndpointsHola.cs`.

**Portal de clientes** (opcional, ATLAS-ADR-0043): si el módulo muestra algo a las personas externas, implementa `MapearEndpointsPortal(RouteGroupBuilder portal)`; el host lo monta en `/api/v1/portal` con sesión, la etiqueta «Portal de clientes» y tu bandera. Cada endpoint del portal debe tener su caso en `AislamientoPortalTests.Casos` (lo propio, lo ajeno de otra organización, lo que queda fuera del alcance y tras revocar o vencer): si no, la suite falla.

### 1.6 Regístralo en el host

1. En `services/api/src/Flit.Atlas.Composicion/Flit.Atlas.Composicion.csproj`, la referencia: `<ProjectReference Include="..\Flit.Atlas.Modulos.<X>\Flit.Atlas.Modulos.<X>.csproj" />`.
2. En `ModulosAtlas.cs`, una línea en `Registrados`: `new Modulo<X>(),`.
3. En `Flit.Atlas.slnx`, el proyecto y su proyecto de pruebas.
4. En `services/api/Dockerfile`, junto a los demás módulos: `COPY services/api/src/Flit.Atlas.Modulos.<X>/Flit.Atlas.Modulos.<X>.csproj services/api/src/Flit.Atlas.Modulos.<X>/`.

Con eso, atlas-api monta sus endpoints, registra su manifiesto y sus scopes, y el migrador migra su contexto, crea su bandera y aplica sus permisos a `plataforma_rol_permiso`. No se toca Program.cs ni el migrador.

### 1.7 Regenera los artefactos generados

Necesitan Docker (el fixture de las pruebas de la API levanta Postgres y Redis):

```bash
ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.ContratoOpenApiTests
ATLAS_ACTUALIZAR_CONTRATOS=1 dotnet test --project services/api/tests/Flit.Atlas.Api.Tests --filter-class Flit.Atlas.Api.Tests.MatrizPermisosTests
```

En PowerShell: `$env:ATLAS_ACTUALIZAR_CONTRATOS = "1"` antes y `Remove-Item Env:ATLAS_ACTUALIZAR_CONTRATOS` después. Versiona `contracts/openapi/atlas.v1.yaml` y `docs/permisos/matriz-rol-permiso.md` en el mismo PR y pásale vacuum al contrato (lo hace el job `contratos` del CI):

```bash
vacuum lint --no-banner --details --no-clip -r contracts/vacuum/openapi.yaml contracts/openapi/atlas.v1.yaml
```

Si publicas eventos, agrégalos a mano en `contracts/asyncapi/atlas-events.v1.yaml` (`ContratoAsyncApiTests` compara).

### 1.8 Pruebas del backend

Copia `Flit.Atlas.Modulos.Hola.Tests` (xUnit v3 + Testcontainers, con el usuario `flit_atlas` como en dev/qa/pdn): contrato del módulo, modelo sin cambios pendientes, migración después del núcleo, RLS forzada, `@pii`, aislamiento por organización (con el predicado, sin él y al escribir) y la llave foránea. Los endpoints se prueban con atlas-api en `Flit.Atlas.Api.Tests` (mira `ModuloHolaTests`: cada prueba usa su propia base con `entorno.BaseNuevaAsync()` para encender la bandera sin afectar a las demás).

## 2. Frontend

### 2.1 Manifiesto `ModuloAtlas`

`apps/web/src/modulos/<x>/modulo.ts`:

```ts
export const moduloHola: ModuloAtlas = {
  id: 'hola',
  nombre: 'Hola',
  descripcion: 'Módulo de ejemplo: …',
  icono: 'ejemplo',
  estado: 'disponible',
  rutas: [{ path: '/hola', permiso: 'atlas.hola.ver', cargar: () => import('./paginas/Saludos') }],
  navegacion: [{ etiqueta: 'Hola', ruta: '/hola', movil: 'mas', orden: 85 }],
  widgetsInicio: [{ id: 'hola-ultimo-saludo', titulo: 'Último saludo', orden: 80, permiso: 'atlas.hola.ver', cargar: () => import('./widgets/UltimoSaludo') }],
  bandera: 'modulo.hola',
  banderaPorDefecto: false,
};
```

- Para mostrar algo en la ficha de cada aplicativo (`/catalogo/<slug>`), declara `pestanasFicha: [{ id: 'responsables', titulo: 'Responsables', orden: 10, permiso: 'atlas.<x>.ver', cargar: () => import('./pestanas/Responsables') }]`. El componente recibe `{ aplicativo: { id, slug, nombre, tipo, grupo } }` y pide sus datos a tu API. Los ids `responsables`, `soporte`, `contratos`, `versiones`, `documentos`, `diagramas` e `ia` ocupan su espacio en la ficha; otro id se agrega al final (ATLAS-ADR-0028).
- Páginas y widgets siempre con `import()` diferido: el bundle inicial no crece (< 200 KB gzip, `pnpm bundle`).
- `permiso` usa los mismos códigos del manifiesto del backend. La PWA solo oculta: el servidor responde 403 o 404.
- La barra inferior del móvil tiene a lo sumo cinco botones: un módulo nuevo va en `movil: 'mas'` salvo que el líder diga otra cosa.
- `banderaPorDefecto` igual a `ActivoPorDefecto` del backend.
- La llamada a la API va en `apps/web/src/modulos/<x>/api.ts` con `pedir` y `enviarJson` de `src/app/api.ts` (cookie del BFF y cabecera antifalsificación incluidas).

### 2.2 Regístralo

Una línea en `apps/web/src/app/modulos/registro.ts` (import y entrada en `MODULOS_REGISTRADOS`). `validarRegistro` revisa ids, banderas y rutas repetidas. Si necesitas un ícono que el shell no tiene, agrégalo en `contrato.ts` (`NombreIcono`) y en `ui/Icono.tsx`.

### 2.3 Reglas de la interfaz

- Móvil primero: sin scroll horizontal a 360 px, objetivos táctiles de 44 px (`min-h-toque`), tablas que pasan a tarjetas.
- CSP estricta (`script-src 'self'; style-src 'self'`): nada de `style` en línea ni librerías que inyecten `<style>`; diálogos con `<dialog>` nativo (`ui/Hoja.tsx`). En WebKit, crear un `<select>` reporta una violación de `style-src-attr`: usa opciones (radios) como la pantalla de la matriz.
- Accesibilidad WCAG 2.1 AA: etiquetas en los campos, regiones con nombre, sin errores críticos de axe en claro y oscuro.
- Textos en español de Colombia, con tuteo.

### 2.4 Pruebas del frontend

- Vitest junto al módulo (`hola.test.tsx`): manifiesto, bandera, permisos y la página con la API simulada (`vi.stubGlobal('fetch', …)`).
- Playwright en los tres perfiles (escritorio, Pixel 7 e iPhone 14). Las de los módulos usan la API simulada con `page.route` (`tests/e2e/api-simulada.ts`) y no necesitan el AppHost; contra la imagen nginx también revisan la CSP:

```bash
cd apps/web
pnpm typecheck && pnpm test && pnpm build && pnpm bundle
docker build -t atlas-web:local .
docker run -d --rm --name atlas-web-local -p 127.0.0.1:4132:4132 -e ATLAS_WEB_PORT=4132 -e ATLAS_API_URL=http://127.0.0.1:1 atlas-web:local
ATLAS_E2E_URL=http://localhost:4132 pnpm e2e modulos
```

## 3. Banderas

- El migrador crea `modulo.<x>` en `atlas.plataforma_modulo` con `ActivoPorDefecto` la primera vez y **nunca** la vuelve a cambiar: encenderla es una decisión de cada ambiente (cada uno tiene su base).
- Mientras no exista la pantalla de configuración (`atlas.plataforma.configurar`, **[por verificar]** en qué HU), se enciende en la base del ambiente, con el usuario del servicio (la tabla no tiene RLS):

```sql
UPDATE atlas.plataforma_modulo SET activo = true, organizaciones_habilitadas = '{}', actualizado_en = now() WHERE id = 'hola';
-- Solo para algunas organizaciones: organizaciones_habilitadas = '{<uuid de la organización>}'
```

- Apagada (o sin la organización de la persona en la lista): los endpoints responden 404 y la PWA no muestra la navegación, los widgets ni la ruta. «hola» viene apagado en local, qa y pdn; en local se enciende con la sentencia de arriba sobre la base del AppHost.

## 4. Checklist de seguridad

- [ ] Cada endpoint con `.RequierePermiso(...)`; ninguno en las listas de públicos o de solo sesión de `AutorizacionTests` sin decisión del líder.
- [ ] Permisos `atlas.<x>.*` con las concesiones de rol revisadas por el líder **[por verificar]** en el PR.
- [ ] Toda tabla con datos de una organización: `organizacion_id`, `AplicarPredicadoDeOrganizacion` y RLS con `FORCE` y `atlas.plataforma_organizacion_visible(...)`.
- [ ] La organización y la persona salen de la sesión; nunca se confía en ids del cliente para el alcance.
- [ ] Columnas con datos personales marcadas `@pii`; ningún dato personal ni contenido de usuario en los logs (usa `LoggerMessage` con ids, nunca textos ni correos).
- [ ] Nada de secretos, IPs internas, tokens ni datos personales reales en código, fixtures o commits (`ejemplo.com`, `Organismo Demo`).
- [ ] Validación de la entrada en el servidor (largos, formatos) y `ValidationProblem` con mensajes en español.
- [ ] Las acciones que cambian datos de negocio quedan en `plataforma_auditoria` cuando el módulo lo necesite (F2 en adelante).
- [ ] Scopes de tokens solo si un cliente que no es el navegador debe usar el módulo.

## 5. Verifica que no tocaste otro módulo

**Las pruebas de arquitectura**, que corren en el CI:

- `ArquitecturaModulosTests` (.NET): tu ensamblado solo depende de `Flit.Atlas.Plataforma`; ningún proyecto fuera de `Flit.Atlas.Composicion` te referencia; tus tablas llevan tu prefijo y tu historial; tus rutas viven bajo `/api/v1/<x>`.
- `tests/arquitectura-modulos.test.ts` (PWA): ningún módulo importa a otro y el shell solo los conoce por `registro.ts`.

**El diff de tu PR** solo toca lo de la tabla del inicio:

```bash
git diff --stat origin/develop... -- services/api/src apps/web/src
git diff --name-only origin/develop... -- services/api/src/Flit.Atlas.Modulos.* apps/web/src/modulos | grep -v "/<x>/"   # debe salir vacío
```

**Todo en verde**, como en el CI:

```bash
dotnet build Flit.Atlas.slnx -c Release          # sin advertencias
dotnet test --solution Flit.Atlas.slnx -c Release  # necesita Docker
docker build -f services/api/Dockerfile --target api .
docker build -f services/api/Dockerfile --target migrador .
```

## 6. Para retirar el ejemplo «hola»

Borra `Flit.Atlas.Modulos.Hola`, `Flit.Atlas.Modulos.Hola.Tests` y `apps/web/src/modulos/hola/`, quita su línea y su referencia en la composición, en `registro.ts`, en la solución y en el Dockerfile, regenera el contrato y la matriz, y agrega una migración de limpieza (o deja la tabla vacía) según decida el líder.
