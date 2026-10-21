---
id: ATLAS-ADR-0016
titulo: Contrato de módulo — manifiestos IModuloAtlas y ModuloAtlas con banderas
estado: propuesto
fecha: 2026-10-21
decisores: [Jorman Copete (líder técnico)]
ambito: atlas
elementos: []
reemplaza: []
reemplazado_por: []
alias: []
fuentes: [prompt maestro §2 (crece por módulos), §5.3, §7.4 y §10]
---

# ATLAS-ADR-0016 · Contrato de módulo — manifiestos IModuloAtlas y ModuloAtlas con banderas

## Contexto y problema

Atlas tiene ocho capacidades que se entregan en fases distintas y deben poder activarse por ambiente y por organización. El principio es que **agregar un módulo no obliga a tocar los demás**. Hace falta un contrato explícito, en el backend y en el frontend, para registrar rutas, permisos, migraciones, eventos, búsqueda, navegación, widgets de inicio y recursos offline.

## Factores de decisión

- Aislamiento entre módulos (compilación, migraciones, prefijos de tabla).
- Permisos declarados por cada módulo y una matriz rol × permiso generada desde el código (F1).
- Carga diferida en la PWA (bundle inicial < 200 KB gzip).
- Activación por banderas (ambiente y organización).

## Opciones consideradas

1. **Manifiesto por módulo**: `IModuloAtlas` en .NET y `ModuloAtlas` en TypeScript, registrados explícitamente en el host y en el shell, más banderas `modulo.<id>`.
2. Descubrimiento por reflexión o escaneo de ensamblados y carpetas.
3. Monolito sin módulos (carpetas por funcionalidad).
4. Microfrontends y un microservicio por módulo.

## Decisión

Opción elegida: **1**.

**Backend** — cada `Flit.Atlas.Modulos.<X>` implementa:

```csharp
public interface IModuloAtlas
{
    string Id { get; }                                    // "soporte"
    string PrefijoTablas { get; }                         // "soporte_"
    IReadOnlyList<PermisoAtlas> Permisos { get; }         // atlas.soporte.ver, ...
    void RegistrarServicios(IServiceCollection servicios, IConfiguration configuracion);
    void MapearEndpoints(IEndpointRouteBuilder rutas);    // bajo /api/v1/<id>, ya filtrado por bandera
    void ConfigurarModelo(ModelBuilder modelo);           // solo entidades con su prefijo
    IReadOnlyList<TipoEvento> EventosPublicados { get; }
    IReadOnlyList<TipoEvento> EventosConsumidos { get; }
    IEnumerable<Type> ProveedoresBusqueda { get; }
    IEnumerable<Type> ProveedoresNotificacion { get; }
}
```

- El host (`Flit.Atlas.Api`) tiene una sola lista explícita de módulos (`Modulos.Registrados`). Agregar un módulo es una línea ahí, más su proyecto.
- Cada módulo usa su propio historial de migraciones de EF Core (`__ef_migraciones_<id>`) sobre el esquema `atlas`. Una prueba de arquitectura verifica que solo cree tablas con su prefijo y que **no referencie otros módulos** (solo `Flit.Atlas.Plataforma`).
- La comunicación entre módulos se hace por eventos (ATLAS-ADR-0014) o por interfaces de lectura publicadas en `Plataforma` (p. ej. `ICatalogoLectura`), nunca por acceso directo a las tablas de otro módulo.
- **Identidad y Plataforma no son módulos**: son núcleo (`Flit.Atlas.Identidad`, `Flit.Atlas.Plataforma`), siempre activos y sin bandera. Esto resuelve la diferencia de nombres entre el §5.3 y el §6 del prompt maestro.

**Frontend** — cada `apps/web/src/modulos/<id>/modulo.ts` exporta un `ModuloAtlas` (forma del prompt maestro §10: `id`, `nombre`, `icono`, `rutas` con `permiso` y `cargar` diferido, `navegacion` con su lugar en el móvil, `widgetsInicio`, `busqueda`, `offline` y `bandera`). El shell los importa desde una lista explícita y filtra por las banderas y permisos que entrega `GET /api/v1/plataforma/yo`.

**Banderas** — en la tabla `plataforma_modulo` (id, activo por ambiente y lista de organizaciones habilitadas). El servidor es la autoridad: un módulo apagado responde 404 en sus endpoints. El frontend solo oculta.

**Documentación** — `docs/como-crear-un-modulo.md` en F1, con plantilla y lista de chequeo.

## Consecuencias

### Positivas

- Módulos aislados y verificables por pruebas de arquitectura; permisos y matriz generados desde los manifiestos.
- La PWA carga solo lo que el usuario puede ver.

### Negativas

- El registro explícito exige editar una lista central (una línea), algo que se acepta frente a la magia de la reflexión.
- Las interfaces de lectura entre módulos se deben diseñar con cuidado para no crear acoplamiento oculto.
- Varios historiales de migraciones en un mismo esquema complican un poco el despliegue (orden por dependencia: núcleo y luego módulos).

## Opciones descartadas y por qué

| Opción | Por qué se descarta |
|---|---|
| Descubrimiento por reflexión | Difícil de seguir y de probar; no funciona bien con trimming o AOT |
| Monolito sin módulos | No permite activar por bandera ni asegura el aislamiento que promete el producto |
| Microfrontends y microservicios por módulo | Demasiada operación para 1–2 desarrolladores y VPS limitadas |

## Más información

- Prompt maestro §10 (ejemplo de manifiesto) y §7.4 (lista base de permisos).
