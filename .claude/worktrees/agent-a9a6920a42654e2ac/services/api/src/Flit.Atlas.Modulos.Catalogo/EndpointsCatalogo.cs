using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Flit.Atlas.Modulos.Catalogo.Datos;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Flit.Atlas.Modulos.Catalogo;

/// <summary>
/// Endpoints de <c>/api/v1/catalogo</c> (HU 2.1, ATLAS-ADR-0028). El host ya puso la etiqueta, la sesión y la bandera.
/// Leer exige <c>atlas.catalogo.ver</c>; crear y eliminar, y cambiar la jerarquía (slug, tipo, padre, grupo),
/// <c>atlas.catalogo.gestionar</c>; cambiar los demás datos, <c>atlas.catalogo.editar</c> sobre el aplicativo o uno de sus
/// ancestros (el responsable implícito lo tiene solo para lo suyo). Cada cambio queda en <c>plataforma_auditoria</c> en la
/// misma transacción.
/// </summary>
internal static class EndpointsCatalogo
{
    public const int LimiteListado = 500;

    public const string AccionCreado = "catalogo.aplicativo_creado";
    public const string AccionActualizado = "catalogo.aplicativo_actualizado";
    public const string AccionEliminado = "catalogo.aplicativo_eliminado";
    public const string RecursoAuditoria = "catalogo.aplicativo";

    [Description("Aplicativo de la ruta de otro (un ancestro o un hijo), para enlazarlo.")]
    public sealed record ReferenciaAplicativo(
        Guid Id,
        [property: Description("Clave natural del aplicativo; es la URL de su ficha.")] string Slug,
        string Nombre);

    [Description("Aplicativo del catálogo en un listado.")]
    public sealed record AplicativoResumen(
        Guid Id,
        [property: Description("Clave natural (minúsculas, números y guiones).")] string Slug,
        string Nombre,
        [property: Description("producto, modulo o servicio.")] string Tipo,
        [property: Description("flit2, flito, satelite, legado o infraestructura.")] string Grupo,
        [property: Description("planeado, activo, en_retiro o retirado.")] string Estado,
        [property: Description("alta, media o baja.")] string Criticidad,
        [property: Description("Si trata datos personales.")] bool ManejaDatosPersonales,
        [property: Description("Padre directo; vacío en un producto.")] Guid? PadreId,
        [property: Description("Descripción; lo que falta confirmar lleva «[por verificar]».")] string? Descripcion,
        IReadOnlyList<string> Tecnologias,
        IReadOnlyList<string> Etiquetas,
        [property: Description("Del producto al padre directo.")] IReadOnlyList<ReferenciaAplicativo> Ancestros,
        [property: Description("Cuántos hijos directos tiene.")] int Hijos);

    [Description("Listado del catálogo filtrado, ordenado por grupo y por la ruta en la jerarquía.")]
    public sealed record ListadoAplicativos(
        [property: Description("Cuántos cumplen el filtro (el listado trae a lo sumo 500).")] int Total,
        IReadOnlyList<AplicativoResumen> Elementos);

    [Description("Qué puede hacer la persona en sesión con este aplicativo.")]
    public sealed record PermisosFicha(
        [property: Description("Cambiar sus datos (atlas.catalogo.editar sobre él o un ancestro, o gestionar).")] bool Editar,
        [property: Description("Crear hijos, cambiar la jerarquía y eliminarlo (atlas.catalogo.gestionar).")] bool Gestionar);

    [Description("Ficha de un aplicativo: sus datos, su ruta en la jerarquía y sus hijos. Las pestañas de los demás módulos las pinta la PWA.")]
    public sealed record FichaAplicativo(
        Guid Id, string Slug, string Nombre,
        [property: Description("producto, modulo o servicio.")] string Tipo,
        [property: Description("flit2, flito, satelite, legado o infraestructura.")] string Grupo,
        [property: Description("planeado, activo, en_retiro o retirado.")] string Estado,
        [property: Description("alta, media o baja.")] string Criticidad,
        bool ManejaDatosPersonales,
        Guid? PadreId,
        string? Descripcion,
        [property: Description("Repositorio de GitHub como dueño/nombre.")] string? Repo,
        [property: Description("Carpeta dentro del repo.")] string? RutaEnRepo,
        IReadOnlyList<string> Tecnologias,
        [property: Description("dev, qa y pdn.")] IReadOnlyList<string> Ambientes,
        IReadOnlyList<string> Etiquetas,
        IReadOnlyList<ReferenciaAplicativo> Ancestros,
        IReadOnlyList<AplicativoResumen> Hijos,
        PermisosFicha Permisos,
        DateTimeOffset CreadoEn,
        DateTimeOffset ActualizadoEn);

    [Description("Datos de un aplicativo nuevo o los cambios de uno existente (se reemplazan todos los campos).")]
    public sealed record SolicitudAplicativo(
        [property: Description("Clave natural única: minúsculas, números y guiones (hasta 80)."), MinLength(1), MaxLength(CatalogoDbContext.LargoSlug)] string Slug,
        [property: Description("Nombre para mostrar (hasta 120)."), MinLength(1), MaxLength(CatalogoDbContext.LargoNombre)] string Nombre,
        [property: Description("producto, modulo o servicio.")] string Tipo,
        [property: Description("Padre: vacío en un producto; un producto para un módulo; un producto o un módulo para un servicio.")] Guid? PadreId,
        [property: Description("flit2, flito, satelite, legado o infraestructura. Obligatorio en un producto; un módulo o servicio hereda el de su padre.")] string? Grupo,
        [property: Description("Descripción (hasta 2000)."), MaxLength(CatalogoDbContext.LargoDescripcion)] string? Descripcion,
        [property: Description("planeado, activo, en_retiro o retirado.")] string Estado,
        [property: Description("alta, media o baja.")] string Criticidad,
        [property: Description("Si trata datos personales.")] bool ManejaDatosPersonales,
        [property: Description("Repositorio de GitHub como dueño/nombre."), MaxLength(CatalogoDbContext.LargoRepo)] string? Repo,
        [property: Description("Carpeta dentro del repo."), MaxLength(CatalogoDbContext.LargoRuta)] string? RutaEnRepo,
        [property: Description("Tecnologías (hasta 20, de hasta 50 caracteres).")] IReadOnlyList<string>? Tecnologias,
        [property: Description("Ambientes donde corre: dev, qa, pdn.")] IReadOnlyList<string>? Ambientes,
        [property: Description("Etiquetas (hasta 20, de hasta 50 caracteres; se guardan en minúsculas).")] IReadOnlyList<string>? Etiquetas);

    [Description("Error del catálogo con un código estable.")]
    public sealed record ErrorCatalogo(
        [property: Description("slug_en_uso, tiene_hijos, fuente_repetida o requiere_permiso (falta el permiso sobre este aplicativo).")] string Codigo,
        string Mensaje);

    public static void Mapear(RouteGroupBuilder grupo)
    {
        var aplicativos = grupo.MapGroup("/aplicativos");

        aplicativos.MapGet("", ListarAsync)
            .RequierePermiso(PermisosCatalogo.Ver)
            .WithName("ListarAplicativos")
            .WithSummary("Catálogo de aplicativos")
            .WithDescription($"Productos, módulos y servicios que cumplen el filtro, ordenados por grupo y por su ruta en la jerarquía (máximo {LimiteListado}). Cada filtro admite varios valores. Requiere atlas.catalogo.ver.");

        aplicativos.MapGet("/{slug}", ObtenerAsync)
            .RequierePermiso(PermisosCatalogo.Ver)
            .WithName("ObtenerFichaAplicativo")
            .WithSummary("Ficha de un aplicativo")
            .WithDescription("Datos del aplicativo, su ruta (ancestros), sus hijos y lo que la persona puede hacer con él. 404 si no existe. Requiere atlas.catalogo.ver.");

        aplicativos.MapPost("", CrearAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("CrearAplicativo")
            .WithSummary("Crear un aplicativo")
            .Produces<ErrorCatalogo>(StatusCodes.Status403Forbidden)
            .WithDescription("Producto, módulo o servicio nuevo. Queda en la auditoría. Requiere atlas.catalogo.gestionar (para un hijo, sobre el padre o un ancestro; para un producto, global).");

        aplicativos.MapPut("/{id:guid}", ActualizarAsync)
            .RequierePermiso(PermisosCatalogo.Editar)
            .WithName("ActualizarAplicativo")
            .WithSummary("Editar un aplicativo")
            .Produces<ErrorCatalogo>(StatusCodes.Status403Forbidden)
            .WithDescription("Reemplaza sus datos. Cambiar slug, tipo, padre o grupo exige además atlas.catalogo.gestionar; el grupo de un producto se propaga a sus descendientes. Queda en la auditoría. Requiere atlas.catalogo.editar sobre el aplicativo o un ancestro.");

        aplicativos.MapDelete("/{id:guid}", EliminarAsync)
            .RequierePermiso(PermisosCatalogo.Gestionar)
            .WithName("EliminarAplicativo")
            .WithSummary("Eliminar un aplicativo sin hijos")
            .Produces<ErrorCatalogo>(StatusCodes.Status403Forbidden)
            .WithDescription("Solo si no tiene hijos (si los tiene, elimínalos primero o márcalo retirado). La semilla no lo vuelve a crear. Queda en la auditoría. Requiere atlas.catalogo.gestionar.");
    }

    // --- Lectura ---

    private static async Task<Results<Ok<ListadoAplicativos>, ValidationProblem>> ListarAsync(
        [Description("Texto a buscar en el nombre, el slug, la descripción, el repo o una etiqueta exacta; sin distinguir tildes ni mayúsculas.")] string? texto,
        [Description("Grupos: flit2, flito, satelite, legado, infraestructura.")] string[]? grupo,
        [Description("Estados: planeado, activo, en_retiro, retirado.")] string[]? estado,
        [Description("Criticidades: alta, media, baja.")] string[]? criticidad,
        [Description("Tipos: producto, modulo, servicio.")] string[]? tipo,
        [Description("Solo los hijos directos de este aplicativo.")] Guid? padreId,
        CatalogoDbContext contexto, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var filtro = new FiltroCatalogo(
            Texto: texto,
            Grupos: Leer(grupo, Valores.Grupos, "grupo", errores),
            Estados: Leer(estado, Valores.Estados, "estado", errores),
            Criticidades: Leer(criticidad, Valores.Criticidades, "criticidad", errores),
            Tipos: Leer(tipo, Valores.Tipos, "tipo", errores),
            PadreId: padreId);
        if ((texto?.Length ?? 0) > 200)
        {
            errores["texto"] = ["El texto de búsqueda va hasta 200 caracteres."];
        }
        if (errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }

        var filas = await ConsultaCatalogo.Filtrar(contexto.Aplicativos.AsNoTracking(), filtro).ToListAsync(ct);
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var elementos = filas
            .OrderBy(a => a.Grupo)
            .ThenBy(a => jerarquia.Ruta(a.Id, a.Nombre), StringComparer.Ordinal)
            .Take(LimiteListado)
            .Select(a => Resumen(a, jerarquia))
            .ToList();
        return TypedResults.Ok(new ListadoAplicativos(filas.Count, elementos));
    }

    private static async Task<Results<Ok<FichaAplicativo>, NotFound>> ObtenerAsync(
        [Description("Slug del aplicativo.")] string slug,
        ClaimsPrincipal usuario, CatalogoDbContext contexto, IPermisosDeSesion permisosDeSesion, CancellationToken ct)
    {
        var aplicativo = await contexto.Aplicativos.AsNoTracking().FirstOrDefaultAsync(a => a.Slug == slug, ct);
        if (aplicativo is null)
        {
            return TypedResults.NotFound();
        }
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var hijos = await contexto.Aplicativos.AsNoTracking().Where(a => a.PadreId == aplicativo.Id).ToListAsync(ct);
        var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
        return TypedResults.Ok(Ficha(aplicativo, jerarquia, hijos, permisos));
    }

    // --- Escritura ---

    private static async Task<Results<Created<FichaAplicativo>, ValidationProblem, JsonHttpResult<ErrorCatalogo>, Conflict<ErrorCatalogo>>> CrearAsync(
        [Description("Datos del aplicativo nuevo.")] SolicitudAplicativo solicitud,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion,
        TimeProvider reloj, CancellationToken ct)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var datos = Validar(solicitud, errores);
        var padre = solicitud.PadreId is { } idPadre ? await contexto.Aplicativos.FirstOrDefaultAsync(a => a.Id == idPadre, ct) : null;
        ValidarJerarquia(datos?.Tipo, solicitud.PadreId, padre, errores);
        if (datos is null || errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }

        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var permisos = await permisosDeSesion.ObtenerAsync(http.User, ct);
        var puede = padre is null
            ? permisos.TieneEn(PermisosCatalogo.Gestionar, AlcanceAsignacion.Global)
            : TieneSobre(permisos, PermisosCatalogo.Gestionar, padre.Id, jerarquia);
        if (!puede)
        {
            return Prohibido(padre is null
                ? "Crear un producto exige atlas.catalogo.gestionar en todo el catálogo."
                : $"Crear un hijo de «{padre.Nombre}» exige atlas.catalogo.gestionar sobre él.");
        }
        if (await contexto.Aplicativos.AnyAsync(a => a.Slug == datos.Slug, ct))
        {
            return SlugEnUso(datos.Slug);
        }

        var aplicativo = new Aplicativo
        {
            Slug = datos.Slug,
            Nombre = datos.Nombre,
            Tipo = datos.Tipo,
            PadreId = padre?.Id,
            Grupo = padre?.Grupo ?? datos.Grupo!.Value,
        };
        Aplicar(aplicativo, datos);
        contexto.Aplicativos.Add(aplicativo);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionCreado, aplicativo, new { aplicativo.Slug, tipo = Valores.Texto(aplicativo.Tipo) }), ct);

        jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        return TypedResults.Created($"/api/v1/catalogo/aplicativos/{aplicativo.Slug}", Ficha(aplicativo, jerarquia, [], permisos));
    }

    private static async Task<Results<Ok<FichaAplicativo>, ValidationProblem, NotFound, JsonHttpResult<ErrorCatalogo>, Conflict<ErrorCatalogo>>> ActualizarAsync(
        [Description("Id del aplicativo.")] Guid id,
        [Description("Todos los datos del aplicativo, con los cambios.")] SolicitudAplicativo solicitud,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion,
        TimeProvider reloj, CancellationToken ct)
    {
        var aplicativo = await contexto.Aplicativos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (aplicativo is null)
        {
            return TypedResults.NotFound();
        }
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var permisos = await permisosDeSesion.ObtenerAsync(http.User, ct);
        if (!TieneSobre(permisos, PermisosCatalogo.Editar, id, jerarquia) && !TieneSobre(permisos, PermisosCatalogo.Gestionar, id, jerarquia))
        {
            return Prohibido($"Editar «{aplicativo.Nombre}» exige atlas.catalogo.editar sobre él o uno de sus ancestros.");
        }

        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var datos = Validar(solicitud, errores);
        var padre = solicitud.PadreId is { } idPadre
            ? await contexto.Aplicativos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == idPadre, ct)
            : null;
        if (solicitud.PadreId == id)
        {
            errores["padreId"] = ["Un aplicativo no puede ser su propio padre."];
        }
        ValidarJerarquia(datos?.Tipo, solicitud.PadreId, padre, errores);
        if (datos is not null && datos.Tipo != aplicativo.Tipo)
        {
            // Los hijos deben seguir siendo válidos bajo el tipo nuevo (un servicio no tiene hijos; un módulo, solo servicios).
            var tiposHijos = await contexto.Aplicativos.Where(a => a.PadreId == id).Select(a => a.Tipo).Distinct().ToListAsync(ct);
            if (tiposHijos.Any(t => !Valores.JerarquiaValida(t, datos.Tipo)))
            {
                errores["tipo"] = ["Sus hijos no quedarían válidos con ese tipo. " + Valores.ReglaJerarquia];
            }
        }
        if (datos is null || errores.Count > 0)
        {
            return TypedResults.ValidationProblem(errores);
        }

        var grupoNuevo = padre?.Grupo ?? datos.Grupo ?? aplicativo.Grupo;
        var cambiaEstructura = datos.Slug != aplicativo.Slug || datos.Tipo != aplicativo.Tipo || solicitud.PadreId != aplicativo.PadreId
            || grupoNuevo != aplicativo.Grupo;
        if (cambiaEstructura)
        {
            // Mover el aplicativo exige gestionar sobre él y, si cambia de padre, también sobre el padre nuevo.
            var puede = TieneSobre(permisos, PermisosCatalogo.Gestionar, id, jerarquia)
                && (padre is null || solicitud.PadreId == aplicativo.PadreId || TieneSobre(permisos, PermisosCatalogo.Gestionar, padre.Id, jerarquia))
                && (solicitud.PadreId is not null || aplicativo.PadreId is null || permisos.TieneEn(PermisosCatalogo.Gestionar, AlcanceAsignacion.Global));
            if (!puede)
            {
                return Prohibido("Cambiar el slug, el tipo, el padre o el grupo exige atlas.catalogo.gestionar.");
            }
        }
        if (datos.Slug != aplicativo.Slug && await contexto.Aplicativos.AnyAsync(a => a.Slug == datos.Slug, ct))
        {
            return SlugEnUso(datos.Slug);
        }

        var cambios = CamposCambiados(aplicativo, datos, solicitud.PadreId, grupoNuevo);
        aplicativo.Slug = datos.Slug;
        aplicativo.Nombre = datos.Nombre;
        aplicativo.Tipo = datos.Tipo;
        aplicativo.PadreId = solicitud.PadreId;
        Aplicar(aplicativo, datos);
        if (grupoNuevo != aplicativo.Grupo)
        {
            aplicativo.Grupo = grupoNuevo;
            // El grupo es del producto: sus módulos y servicios lo siguen.
            var descendientes = jerarquia.Descendientes(id).ToList();
            await foreach (var hijo in contexto.Aplicativos.Where(a => descendientes.Contains(a.Id)).AsAsyncEnumerable().WithCancellation(ct))
            {
                hijo.Grupo = grupoNuevo;
            }
        }
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionActualizado, aplicativo, new { aplicativo.Slug, campos = cambios }), ct);

        jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var hijos = await contexto.Aplicativos.AsNoTracking().Where(a => a.PadreId == id).ToListAsync(ct);
        return TypedResults.Ok(Ficha(aplicativo, jerarquia, hijos, permisos));
    }

    private static async Task<Results<NoContent, NotFound, JsonHttpResult<ErrorCatalogo>, Conflict<ErrorCatalogo>>> EliminarAsync(
        [Description("Id del aplicativo.")] Guid id,
        HttpContext http, CatalogoDbContext contexto, PlataformaDbContext plataforma, IPermisosDeSesion permisosDeSesion,
        TimeProvider reloj, CancellationToken ct)
    {
        var aplicativo = await contexto.Aplicativos.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (aplicativo is null)
        {
            return TypedResults.NotFound();
        }
        var jerarquia = await ConsultaCatalogo.JerarquiaAsync(contexto, ct);
        var permisos = await permisosDeSesion.ObtenerAsync(http.User, ct);
        if (!TieneSobre(permisos, PermisosCatalogo.Gestionar, id, jerarquia))
        {
            return Prohibido($"Eliminar «{aplicativo.Nombre}» exige atlas.catalogo.gestionar.");
        }
        if (jerarquia.Hijos(id) > 0)
        {
            return TypedResults.Conflict(new ErrorCatalogo("tiene_hijos",
                $"«{aplicativo.Nombre}» tiene {jerarquia.Hijos(id)} hijos: elimínalos primero o márcalo como retirado."));
        }
        contexto.Aplicativos.Remove(aplicativo);
        await GuardarConAuditoriaAsync(contexto, plataforma, Evento(http, reloj, AccionEliminado, aplicativo, new { aplicativo.Slug, aplicativo.Nombre }), ct);
        return TypedResults.NoContent();
    }

    // --- Permisos por recurso ---

    /// <summary>
    /// ¿Tiene el permiso sobre el aplicativo? Vale una concesión global o una con alcance a él o a uno de sus ancestros: el
    /// responsable de un producto también edita sus módulos y servicios.
    /// </summary>
    internal static bool TieneSobre(PermisosEfectivos permisos, string permiso, Guid id, Jerarquia jerarquia) =>
        permisos.TieneEn(permiso, Alcance(id))
        || jerarquia.Ancestros(id).Any(a => permisos.TieneEn(permiso, Alcance(a.Id)));

    private static AlcanceAsignacion Alcance(Guid id) => new(TiposAlcance.Aplicativo, id.ToString());

    // --- Validación ---

    /// <summary>La solicitud ya interpretada; <c>null</c> si algún campo no es válido (los errores quedan en el diccionario).</summary>
    private sealed record DatosValidos(
        string Slug, string Nombre, TipoAplicativo Tipo, GrupoAplicativo? Grupo, string? Descripcion, EstadoAplicativo Estado,
        Criticidad Criticidad, bool ManejaDatosPersonales, string? Repo, string? RutaEnRepo, List<string> Tecnologias,
        List<string> Ambientes, List<string> Etiquetas);

    private static DatosValidos? Validar(SolicitudAplicativo s, Dictionary<string, string[]> errores)
    {
        var slug = (s.Slug ?? string.Empty).Trim();
        if (!Valores.SlugValido(slug))
        {
            errores["slug"] = [$"El slug va en minúsculas, números y guiones (por ejemplo flit2-comparendos), hasta {CatalogoDbContext.LargoSlug} caracteres."];
        }
        var nombre = (s.Nombre ?? string.Empty).Trim();
        if (nombre.Length is 0 or > CatalogoDbContext.LargoNombre)
        {
            errores["nombre"] = [$"El nombre es obligatorio y va hasta {CatalogoDbContext.LargoNombre} caracteres."];
        }
        var tipo = Uno(s.Tipo, Valores.Tipos, "tipo", errores);
        var grupo = s.Grupo is null ? (GrupoAplicativo?)null : Uno(s.Grupo, Valores.Grupos, "grupo", errores);
        if (tipo == TipoAplicativo.Producto && s.Grupo is null)
        {
            errores["grupo"] = ["Un producto necesita su grupo."];
        }
        var estado = Uno(s.Estado, Valores.Estados, "estado", errores);
        var criticidad = Uno(s.Criticidad, Valores.Criticidades, "criticidad", errores);
        var descripcion = Opcional(s.Descripcion);
        if ((descripcion?.Length ?? 0) > CatalogoDbContext.LargoDescripcion)
        {
            errores["descripcion"] = [$"La descripción va hasta {CatalogoDbContext.LargoDescripcion} caracteres."];
        }
        var repo = Opcional(s.Repo);
        if (repo is not null && !Valores.RepoValido(repo))
        {
            errores["repo"] = ["El repo va como dueño/nombre (por ejemplo flitsas/flit)."];
        }
        var ruta = Opcional(s.RutaEnRepo)?.Trim('/');
        if ((ruta?.Length ?? 0) > CatalogoDbContext.LargoRuta || (ruta?.Contains("..", StringComparison.Ordinal) ?? false))
        {
            errores["rutaEnRepo"] = [$"La ruta es una carpeta del repo, sin «..», hasta {CatalogoDbContext.LargoRuta} caracteres."];
        }
        var tecnologias = Lista(s.Tecnologias, minusculas: false, "tecnologias", errores);
        var etiquetas = Lista(s.Etiquetas, minusculas: true, "etiquetas", errores);
        var ambientes = Valores.Limpiar(s.Ambientes, minusculas: true);
        if (ambientes.Any(a => !Valores.Ambientes.Contains(a)))
        {
            errores["ambientes"] = [$"Ambientes válidos: {string.Join(", ", Valores.Ambientes)}."];
        }
        if (errores.Count > 0 || tipo is null || estado is null || criticidad is null)
        {
            return null;
        }
        return new DatosValidos(slug, nombre, tipo.Value, grupo, descripcion, estado.Value, criticidad.Value, s.ManejaDatosPersonales,
            repo, string.IsNullOrEmpty(ruta) ? null : ruta, tecnologias, [.. Valores.Ambientes.Where(ambientes.Contains)], etiquetas);
    }

    private static void ValidarJerarquia(TipoAplicativo? tipo, Guid? padreId, Aplicativo? padre, Dictionary<string, string[]> errores)
    {
        if (padreId is not null && padre is null)
        {
            errores["padreId"] = ["El padre no existe."];
            return;
        }
        if (tipo is { } t && !errores.ContainsKey("padreId") && !Valores.JerarquiaValida(t, padre?.Tipo))
        {
            errores["padreId"] = [Valores.ReglaJerarquia];
        }
    }

    private static T? Uno<T>(string? texto, IReadOnlyDictionary<string, T> valores, string campo, Dictionary<string, string[]> errores) where T : struct
    {
        if (texto is not null && valores.TryGetValue(texto.Trim(), out var valor))
        {
            return valor;
        }
        errores[campo] = [$"Valores válidos: {string.Join(", ", valores.Keys)}."];
        return null;
    }

    private static List<T>? Leer<T>(string[]? textos, IReadOnlyDictionary<string, T> valores, string campo, Dictionary<string, string[]> errores)
    {
        if (textos is null || textos.Length == 0)
        {
            return null;
        }
        var lista = new List<T>();
        foreach (var texto in textos.SelectMany(t => (t ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!valores.TryGetValue(texto, out var valor))
            {
                errores[campo] = [$"Valores válidos: {string.Join(", ", valores.Keys)}."];
                return null;
            }
            lista.Add(valor);
        }
        return lista;
    }

    private static List<string> Lista(IReadOnlyList<string>? valores, bool minusculas, string campo, Dictionary<string, string[]> errores)
    {
        var lista = Valores.Limpiar(valores, minusculas);
        if (lista.Count > CatalogoDbContext.MaximoElementos || lista.Any(v => v.Length > CatalogoDbContext.LargoEtiqueta))
        {
            errores[campo] = [$"Hasta {CatalogoDbContext.MaximoElementos} valores de hasta {CatalogoDbContext.LargoEtiqueta} caracteres."];
        }
        return lista;
    }

    private static string? Opcional(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static void Aplicar(Aplicativo a, DatosValidos d)
    {
        a.Descripcion = d.Descripcion;
        a.Estado = d.Estado;
        a.Criticidad = d.Criticidad;
        a.ManejaDatosPersonales = d.ManejaDatosPersonales;
        a.Repo = d.Repo;
        a.RutaEnRepo = d.RutaEnRepo;
        a.Tecnologias = d.Tecnologias;
        a.Ambientes = d.Ambientes;
        a.Etiquetas = d.Etiquetas;
    }

    /// <summary>Nombres de los campos que cambian (para la auditoría: nunca los valores).</summary>
    private static List<string> CamposCambiados(Aplicativo a, DatosValidos d, Guid? padreId, GrupoAplicativo grupo)
    {
        var campos = new List<string>();
        void Si(bool cambia, string campo)
        {
            if (cambia) campos.Add(campo);
        }
        Si(a.Slug != d.Slug, "slug");
        Si(a.Nombre != d.Nombre, "nombre");
        Si(a.Tipo != d.Tipo, "tipo");
        Si(a.PadreId != padreId, "padreId");
        Si(a.Grupo != grupo, "grupo");
        Si(a.Descripcion != d.Descripcion, "descripcion");
        Si(a.Estado != d.Estado, "estado");
        Si(a.Criticidad != d.Criticidad, "criticidad");
        Si(a.ManejaDatosPersonales != d.ManejaDatosPersonales, "manejaDatosPersonales");
        Si(a.Repo != d.Repo, "repo");
        Si(a.RutaEnRepo != d.RutaEnRepo, "rutaEnRepo");
        Si(!a.Tecnologias.SequenceEqual(d.Tecnologias), "tecnologias");
        Si(!a.Ambientes.SequenceEqual(d.Ambientes), "ambientes");
        Si(!a.Etiquetas.SequenceEqual(d.Etiquetas), "etiquetas");
        return campos;
    }

    private static JsonHttpResult<ErrorCatalogo> Prohibido(string mensaje) =>
        TypedResults.Json(new ErrorCatalogo("requiere_permiso", mensaje), statusCode: StatusCodes.Status403Forbidden);

    private static Conflict<ErrorCatalogo> SlugEnUso(string slug) =>
        TypedResults.Conflict(new ErrorCatalogo("slug_en_uso", $"Ya hay un aplicativo con el slug «{slug}»."));

    // --- Auditoría ---

    private static EventoAuditoria Evento(HttpContext http, TimeProvider reloj, string accion, Aplicativo aplicativo, object datos) => new()
    {
        OcurridoEn = reloj.GetUtcNow(),
        ActorTipo = TipoActor.Persona,
        ActorId = http.User.FindFirstValue(EndpointsPlataforma.ClaimPersona),
        OrganizacionId = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var organizacion) ? organizacion : null,
        Accion = accion,
        RecursoTipo = RecursoAuditoria,
        RecursoId = aplicativo.Id.ToString(),
        Ip = http.Connection.RemoteIpAddress,
        // Solo datos de la empresa (slug, nombre, campos cambiados): nunca contenido de personas.
        Datos = JsonSerializer.Serialize(datos, JsonSerializerOptions.Web),
        CorrelacionId = http.TraceIdentifier,
    };

    /// <summary>
    /// Guarda el cambio del catálogo y su evento de auditoría en una sola transacción: el contexto de la plataforma usa la
    /// misma conexión del catálogo. Si algo falla, no queda ni el cambio ni la auditoría.
    /// </summary>
    private static async Task GuardarConAuditoriaAsync(CatalogoDbContext contexto, PlataformaDbContext plataforma, EventoAuditoria evento, CancellationToken ct)
    {
        await using var transaccion = await contexto.Database.BeginTransactionAsync(ct);
        await contexto.SaveChangesAsync(ct);
        plataforma.Database.SetDbConnection(contexto.Database.GetDbConnection());
        await plataforma.Database.UseTransactionAsync(transaccion.GetDbTransaction(), ct);
        plataforma.Auditoria.Add(evento);
        await plataforma.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
    }

    // --- Proyecciones ---

    internal static AplicativoResumen Resumen(Aplicativo a, Jerarquia jerarquia) => new(
        a.Id, a.Slug, a.Nombre, Valores.Texto(a.Tipo), Valores.Texto(a.Grupo), Valores.Texto(a.Estado), Valores.Texto(a.Criticidad),
        a.ManejaDatosPersonales, a.PadreId, a.Descripcion, a.Tecnologias, a.Etiquetas,
        [.. jerarquia.Ancestros(a.Id).Select(n => new ReferenciaAplicativo(n.Id, n.Slug, n.Nombre))], jerarquia.Hijos(a.Id));

    private static FichaAplicativo Ficha(Aplicativo a, Jerarquia jerarquia, IReadOnlyList<Aplicativo> hijos, PermisosEfectivos permisos) => new(
        a.Id, a.Slug, a.Nombre, Valores.Texto(a.Tipo), Valores.Texto(a.Grupo), Valores.Texto(a.Estado), Valores.Texto(a.Criticidad),
        a.ManejaDatosPersonales, a.PadreId, a.Descripcion, a.Repo, a.RutaEnRepo, a.Tecnologias, a.Ambientes, a.Etiquetas,
        [.. jerarquia.Ancestros(a.Id).Select(n => new ReferenciaAplicativo(n.Id, n.Slug, n.Nombre))],
        [.. hijos.OrderBy(h => h.Tipo).ThenBy(h => h.Nombre, StringComparer.OrdinalIgnoreCase).Select(h => Resumen(h, jerarquia))],
        new PermisosFicha(
            TieneSobre(permisos, PermisosCatalogo.Editar, a.Id, jerarquia) || TieneSobre(permisos, PermisosCatalogo.Gestionar, a.Id, jerarquia),
            TieneSobre(permisos, PermisosCatalogo.Gestionar, a.Id, jerarquia)),
        a.CreadoEn, a.ActualizadoEn);
}
