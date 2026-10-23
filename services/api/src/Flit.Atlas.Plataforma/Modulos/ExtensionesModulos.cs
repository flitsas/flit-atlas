using System.Security.Claims;
using System.Text.RegularExpressions;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Plataforma.Modulos;

/// <summary>Metadato de los endpoints de un módulo: el contrato OpenAPI documenta su 404 por bandera.</summary>
public sealed class MetadatoModulo(string id)
{
    public string Id { get; } = id;

    public string Bandera => ExtensionesModulos.Bandera(Id);
}

/// <summary>
/// Registro de los módulos funcionales en el host (ATLAS-ADR-0016 y ATLAS-ADR-0026). atlas-api llama a
/// <see cref="AgregarModulos"/> y <see cref="MapModulos"/>; el migrador, a <see cref="RegistrarDatosModulos"/>,
/// <see cref="ContextoDatosDe"/> y <see cref="RegistrarBanderasAsync"/>. Todos reciben la misma lista.
/// </summary>
public static partial class ExtensionesModulos
{
    /// <summary>Ids que no puede usar un módulo: son núcleo o rutas que ya existen bajo <c>/api/v1</c>.</summary>
    private static readonly HashSet<string> Reservados = new(StringComparer.Ordinal) { "plataforma", "identidad", "version" };

    public static string Bandera(string id) => $"modulo.{id}";

    public static string TablaHistorial(string id) => $"__ef_migraciones_{id}";

    /// <summary>
    /// Revisa que los módulos no choquen entre sí ni con el núcleo: ids únicos en snake_case y no reservados, manifiesto
    /// del mismo id con permisos <c>atlas.&lt;id&gt;.*</c>, eventos y scopes <c>atlas.&lt;id&gt;.*</c>, notificaciones
    /// <c>&lt;id&gt;.*</c> y contexto derivado de <see cref="ContextoAtlas"/>. Falla al arrancar, nombrando el problema.
    /// </summary>
    public static void Validar(IEnumerable<IModuloAtlas> modulos)
    {
        ArgumentNullException.ThrowIfNull(modulos);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var modulo in modulos)
        {
            if (!FormatoId().IsMatch(modulo.Id) || Reservados.Contains(modulo.Id))
            {
                throw new InvalidOperationException($"«{modulo.Id}» no es un id de módulo válido (snake_case y distinto de {string.Join(", ", Reservados)}).");
            }
            if (!ids.Add(modulo.Id))
            {
                throw new InvalidOperationException($"El módulo «{modulo.Id}» está registrado dos veces.");
            }
            if (modulo.Permisos.Modulo != modulo.Id)
            {
                throw new InvalidOperationException($"El manifiesto de permisos de «{modulo.Id}» dice ser de «{modulo.Permisos.Modulo}».");
            }
            var prefijo = $"atlas.{modulo.Id}.";
            foreach (var permiso in modulo.Permisos.Permisos.Where(p => !p.Codigo.StartsWith(prefijo, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"El permiso «{permiso.Codigo}» de «{modulo.Id}» debe empezar por «{prefijo}».");
            }
            foreach (var evento in modulo.EventosPublicados.Where(e => !e.Nombre.StartsWith(prefijo, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"El evento «{evento.Nombre}» que publica «{modulo.Id}» debe empezar por «{prefijo}».");
            }
            foreach (var tipo in modulo.TiposNotificacion.Where(t => !t.Codigo.StartsWith($"{modulo.Id}.", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"El tipo de notificación «{tipo.Codigo}» de «{modulo.Id}» debe empezar por «{modulo.Id}.».");
            }
            foreach (var scope in modulo.Scopes.Where(s => s.Nombre != $"atlas.{modulo.Id}" && !s.Nombre.StartsWith(prefijo, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"El scope «{scope.Nombre}» de «{modulo.Id}» debe ser «atlas.{modulo.Id}» o empezar por «{prefijo}».");
            }
            if (modulo.ContextoDatos is { } contexto && !contexto.IsSubclassOf(typeof(ContextoAtlas)))
            {
                throw new InvalidOperationException($"El contexto de datos de «{modulo.Id}» ({contexto.Name}) debe derivar de ContextoAtlas.");
            }
        }
    }

    /// <summary>
    /// atlas-api: valida los módulos y registra de cada uno el manifiesto de permisos, sus datos y sus servicios.
    /// </summary>
    public static IServiceCollection AgregarModulos(
        this IServiceCollection servicios, IConfiguration configuracion, string cadenaConexion, IReadOnlyList<IModuloAtlas> modulos)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(modulos);
        Validar(modulos);
        foreach (var modulo in modulos)
        {
            servicios.AddSingleton(modulo);
            servicios.AgregarManifiestoPermisos(modulo.Permisos);
            modulo.RegistrarDatos(servicios, cadenaConexion);
            modulo.RegistrarServicios(servicios, configuracion);
        }
        return servicios;
    }

    /// <summary>Migrador y pruebas: valida los módulos y registra solo sus contextos de datos.</summary>
    public static IServiceCollection RegistrarDatosModulos(this IServiceCollection servicios, string cadenaConexion, IReadOnlyList<IModuloAtlas> modulos)
    {
        ArgumentNullException.ThrowIfNull(modulos);
        Validar(modulos);
        foreach (var modulo in modulos)
        {
            modulo.RegistrarDatos(servicios, cadenaConexion);
        }
        return servicios;
    }

    /// <summary>
    /// Monta cada módulo registrado en <c>/api/v1/&lt;id&gt;</c>: etiqueta del módulo, sesión obligatoria y la bandera
    /// <c>modulo.&lt;id&gt;</c>, que el servidor hace cumplir con un 404 (el frontend solo oculta, ATLAS-ADR-0016).
    /// </summary>
    public static IEndpointRouteBuilder MapModulos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        foreach (var modulo in rutas.ServiceProvider.GetServices<IModuloAtlas>())
        {
            var grupo = rutas.MapGroup($"/api/v1/{modulo.Id}")
                .WithTags(modulo.Nombre)
                .RequireAuthorization()
                .WithMetadata(new MetadatoModulo(modulo.Id))
                .AddEndpointFilter(new FiltroBandera(modulo));
            modulo.MapearEndpoints(grupo);
        }
        return rutas;
    }

    /// <summary>El contexto de datos del módulo, resuelto del contenedor (para migrarlo), o <c>null</c> si no tiene tablas.</summary>
    public static DbContext? ContextoDatosDe(IServiceProvider servicios, IModuloAtlas modulo)
    {
        ArgumentNullException.ThrowIfNull(modulo);
        return modulo.ContextoDatos is { } tipo ? (DbContext)servicios.GetRequiredService(tipo) : null;
    }

    /// <summary>
    /// Migrador y pruebas: corre <see cref="IModuloAtlas.SembrarAsync"/> de cada módulo, en el orden de la lista, cada uno
    /// en su propio alcance de servicios. Va después de todas las migraciones.
    /// </summary>
    public static async Task SembrarModulosAsync(IServiceProvider servicios, IEnumerable<IModuloAtlas> modulos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(modulos);
        foreach (var modulo in modulos)
        {
            var alcance = servicios.CreateAsyncScope();
            await using (alcance.ConfigureAwait(false))
            {
                await modulo.SembrarAsync(alcance.ServiceProvider, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Crea en <c>plataforma_modulo</c> la bandera de cada módulo que aún no la tiene, con su
    /// <see cref="IModuloAtlas.ActivoPorDefecto"/>. Nunca cambia una fila existente: encenderla o apagarla es una decisión
    /// de cada ambiente. Devuelve cuántas creó.
    /// </summary>
    public static async Task<int> RegistrarBanderasAsync(
        PlataformaDbContext contexto, IEnumerable<IModuloAtlas> modulos, TimeProvider reloj, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(modulos);
        ArgumentNullException.ThrowIfNull(reloj);
        var existentes = await contexto.Modulos.Select(m => m.Id).ToListAsync(ct).ConfigureAwait(false);
        var nuevas = modulos.Where(m => !existentes.Contains(m.Id))
            .Select(m => new Modulo { Id = m.Id, Activo = m.ActivoPorDefecto, ActualizadoEn = reloj.GetUtcNow() })
            .ToList();
        contexto.Modulos.AddRange(nuevas);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return nuevas.Count;
    }

    /// <summary>
    /// Regla de la bandera (la misma de <c>GET /api/v1/plataforma/banderas</c>): sin fila, el valor por defecto del
    /// módulo; con fila, activa y sin restricción de organizaciones o con la de la persona en la lista.
    /// </summary>
    public static bool BanderaActiva(Modulo? fila, bool porDefecto, Guid organizacion) =>
        fila is null ? porDefecto : fila.Activo && (fila.OrganizacionesHabilitadas.Count == 0 || fila.OrganizacionesHabilitadas.Contains(organizacion));

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex FormatoId();

    /// <summary>404 si la bandera del módulo está apagada para la organización de la persona en sesión.</summary>
    private sealed class FiltroBandera(IModuloAtlas modulo) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            var contexto = http.RequestServices.GetRequiredService<PlataformaDbContext>();
            var fila = await contexto.Modulos.AsNoTracking().FirstOrDefaultAsync(m => m.Id == modulo.Id, http.RequestAborted).ConfigureAwait(false);
            var organizacion = Guid.TryParse(http.User.FindFirstValue(EndpointsPlataforma.ClaimOrganizacion), out var id) ? id : Guid.Empty;
            if (!BanderaActiva(fila, modulo.ActivoPorDefecto, organizacion))
            {
                return Results.NotFound();
            }
            return await next(context).ConfigureAwait(false);
        }
    }
}
