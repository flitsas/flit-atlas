using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Emergencia;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Identidad.Entra;
using Flit.Atlas.Identidad.Externos;
using Flit.Atlas.Identidad.Personas;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Identidad.Tokens;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;

namespace Flit.Atlas.Identidad;

public static class EndpointsIdentidad
{
    /// <summary>Cabecera que exigen las peticiones que cambian estado (ATLAS-ADR-0008).</summary>
    public const string CabeceraCsrf = "X-Atlas-Csrf";

    /// <summary>
    /// Redirect URI de Entra ID, la que quedó registrada en Azure para cada ambiente. Está bajo <c>/api/</c>, así que
    /// los proxies de la web ya la reenvían a la API.
    /// </summary>
    public const string RutaRetornoEntra = "/api/auth/callback/azure-ad";

    /// <param name="Reautenticar">La PWA vuelve a pedir el ingreso porque una acción sensible lo exigió.</param>
    /// <param name="Administrador">
    /// Solo local y dev: suma el grupo de <see cref="ExtensionesIdentidad.ClaveSimuladoGrupoAdministracion"/>, que da
    /// <c>atlas.superadmin</c>, para probar la administración sin Entra (HU 1.5). Sin esa variable, se ignora.
    /// </param>
    public sealed record SolicitudSimulada(string Correo, string Nombre, Guid[]? Grupos, bool Reautenticar = false, bool Administrador = false);

    /// <param name="Roles">Roles vigentes en este momento (incluye atlas.lector de todo empleado y los implícitos).</param>
    /// <param name="Permisos">Permisos efectivos en este momento, calculados en el servidor (ATLAS-ADR-0023).</param>
    /// <param name="Emergencia">Sesión de la cuenta de administrador de emergencia (ATLAS-ADR-0027).</param>
    [Description("Usuario de la sesión actual: persona, organización, proveedor con el que entró, roles vigentes y permisos efectivos.")]
    public sealed record Yo(
        Guid Sub, Guid PersonaId, string Nombre, string TipoPrincipal, Guid OrganizacionId, string Proveedor,
        [property: Description("Códigos de los roles vigentes en este momento, ordenados; incluye atlas.lector de todo empleado.")]
        IReadOnlyList<string> Roles,
        [property: Description("Permisos efectivos (atlas.<modulo>.<accion>) en este momento, ordenados: la PWA filtra la navegación con ellos. Se recalculan en cada petición.")]
        IReadOnlyList<string> Permisos,
        [property: Description("true si la sesión es de la cuenta de administrador de emergencia (ATLAS-ADR-0027): la PWA muestra el aviso.")]
        bool Emergencia,
        [property: Description("URL relativa de la foto de la persona (sincronizada con Graph, ATLAS-ADR-0030), versionada por su hash; vacía si no tiene.")]
        string? Foto);

    public static IEndpointRouteBuilder MapIdentidad(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);
        var proveedores = rutas.ServiceProvider.GetService(typeof(ProveedoresHabilitados)) as ProveedoresHabilitados
            ?? throw new InvalidOperationException("Falta AgregarIdentidad en el arranque.");

        var identidad = rutas.MapGroup("/api/v1/identidad").WithTags("Identidad").WithMetadata(new MetadatoLimiteTasa());

        identidad.MapGet("/proveedores", () => proveedores)
            .WithName("ListarProveedoresDeIngreso")
            .WithSummary("Proveedores de ingreso habilitados")
            .WithDescription("Qué botones de ingreso muestra la PWA en este ambiente. No requiere sesión.");

        if (proveedores.Microsoft)
        {
            // Navegación completa del navegador (no fetch): termina en RutaRetornoEntra y vuelve a la PWA.
            // Con reautenticar=true, Entra vuelve a pedir las credenciales (prompt=login) aunque haya sesión de Microsoft.
            identidad.MapGet("/entrar/microsoft", (
                    [Description("Ruta local de la PWA a la que se vuelve después del ingreso (por defecto «/»).")] string? volver,
                    [Description("true cuando una acción sensible pidió reautenticación (responde 401 con codigo reautenticacion_requerida).")] bool? reautenticar) =>
                {
                    var propiedades = new AuthenticationProperties { RedirectUri = RutaLocal(volver) };
                    if (reautenticar == true)
                    {
                        propiedades.Items[IngresoEntra.ClaveReautenticar] = "1";
                    }
                    return Results.Challenge(propiedades, [ClaimsAtlas.EsquemaEntra]);
                })
                .WithName("EntrarConMicrosoft")
                .WithSummary("Iniciar sesión con Microsoft")
                .WithDescription(
                    "Navegación completa del navegador (no fetch). Redirige a Entra ID (OIDC con PKCE); al volver por " +
                    $"{RutaRetornoEntra} crea una sesión nueva y lleva a «volver», que solo puede ser una ruta local de la PWA. " +
                    "Con reautenticar=true, Entra vuelve a pedir las credenciales (prompt=login).")
                .Produces(StatusCodes.Status302Found);
        }

        if (proveedores.Simulado)
        {
            identidad.MapPost("/simulado/entrar", async (SolicitudSimulada solicitud, AprovisionadorEmpleados aprovisionador,
                RegistroSeguridad registro, HttpContext http, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(solicitud.Correo) || string.IsNullOrWhiteSpace(solicitud.Nombre))
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["correo"] = ["El correo y el nombre son obligatorios."],
                    });
                }
                var correo = solicitud.Correo.Trim().ToLowerInvariant();
                var grupos = (solicitud.Grupos ?? []).ToList();
                if (solicitud.Administrador
                    && Guid.TryParse(http.RequestServices.GetRequiredService<IConfiguration>()[ExtensionesIdentidad.ClaveSimuladoGrupoAdministracion], out var grupoAdmin))
                {
                    await AsegurarGrupoAdministracionAsync(http.RequestServices.GetRequiredService<PlataformaDbContext>(), grupoAdmin, ct);
                    grupos.Add(grupoAdmin);
                }
                var cuenta = new CuentaEmpleado(
                    AprovisionadorEmpleados.ProveedorSimulado, correo, null, correo, solicitud.Nombre.Trim(),
                    grupos, null, null, [], Reautenticacion: solicitud.Reautenticar);
                try
                {
                    var principal = await aprovisionador.AprovisionarAsync(cuenta, ct);
                    var propiedades = new AuthenticationProperties { IsPersistent = true };
                    propiedades.SetString(AlmacenSesiones.ClavesPropiedades.Agente, ResumenAgente.De(http.Request.Headers.UserAgent));
                    await http.SignInAsync(ClaimsAtlas.EsquemaCookie, principal, propiedades);
                    return Results.NoContent();
                }
                catch (CorreoEnUsoException)
                {
                    return Results.Conflict(new { error = "correo_en_uso" });
                }
                catch (IngresoNoPermitidoException ex)
                {
                    await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, new DatosEventoSeguridad
                    {
                        Detalle = new { proveedor = AprovisionadorEmpleados.ProveedorSimulado, motivo = ex.Motivo },
                    }, ct);
                    // Mensaje genérico: no revela si la cuenta existe, está suspendida o bloqueada.
                    return Results.Json(new { error = "ingreso_no_permitido" }, statusCode: StatusCodes.Status403Forbidden);
                }
            })
            // Solo existe en local y dev: no es parte del contrato de qa y pdn (ATLAS-ADR-0021).
            .ExcludeFromDescription();
        }

        identidad.MapPost("/salir", async (HttpContext http, RegistroSeguridad registro, CancellationToken ct) =>
        {
            await EndpointsSesiones.SalirAsync(http, registro, ct);
            return Results.NoContent();
        })
            .WithName("CerrarSesion")
            .WithSummary("Cerrar sesión")
            .WithDescription(
                "Revoca la sesión en el servidor, borra la cookie y envía Clear-Site-Data para que el navegador limpie " +
                "caché, almacenamiento y cookies del origen. Responde 204 aunque no haya sesión.")
            .Produces(StatusCodes.Status204NoContent);

        identidad.MapSesiones();
        // Servidor de tokens (HU 1.3c): /connect/* fuera del contrato y GET /api/v1/identidad/token/yo con Bearer.
        rutas.MapServidorTokens(identidad);
        identidad.MapCuentasExternas();
        identidad.MapEmergencia();

        rutas.MapGet("/api/v1/plataforma/yo", async (ClaimsPrincipal usuario, IPermisosDeSesion permisosDeSesion, PlataformaDbContext plataforma,
                CancellationToken ct) =>
            {
                var permisos = await permisosDeSesion.ObtenerAsync(usuario, ct);
                var personaId = Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.PersonaId)!);
                var fotoHash = await plataforma.Personas.AsNoTracking().Where(p => p.Id == personaId).Select(p => p.FotoHash).FirstOrDefaultAsync(ct);
                return new Yo(
                    Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.Sub)!),
                    personaId,
                    usuario.FindFirstValue(ClaimsAtlas.Nombre) ?? string.Empty,
                    usuario.FindFirstValue(ClaimsAtlas.TipoPrincipal)!,
                    Guid.Parse(usuario.FindFirstValue(ClaimsAtlas.OrganizacionId)!),
                    usuario.FindFirstValue(ClaimsAtlas.Proveedor)!,
                    permisos.Roles,
                    permisos.Permisos,
                    usuario.FindFirstValue(ClaimsAtlas.Proveedor) == CuentaEmergencia.Proveedor,
                    EndpointsDirectorio.UrlFoto(personaId, fotoHash));
            })
            .RequireAuthorization()
            .WithMetadata(new MetadatoLimiteTasa())
            .WithName("ObtenerUsuarioActual")
            .WithTags("Plataforma")
            .WithSummary("Usuario en sesión")
            .WithDescription(
                "Persona, organización, proveedor de ingreso, roles vigentes y permisos efectivos de la sesión actual. Roles " +
                "y permisos se calculan en el servidor en cada petición con las asignaciones vigentes: un rol vencido o retirado " +
                "ya no aparece (ATLAS-ADR-0023).");

        rutas.MapRoles();
        rutas.MapDirectorio();
        rutas.MapAdministracionExternos();
        rutas.MapErroresCliente();
        return rutas;
    }

    /// <summary>
    /// Antifalsificación de la API (ATLAS-ADR-0008), además de SameSite=Strict: toda petición a <c>/api/</c> que
    /// cambia estado trae la cabecera propia (un formulario de otro sitio no puede ponerla sin CORS, y Atlas no habilita
    /// CORS) y, si el navegador manda <c>Origin</c>, debe ser el mismo host. Se exceptúa <see cref="RutaRetornoEntra"/>:
    /// Microsoft la llama con un POST desde su propio origen, y la protegen el <c>state</c>, el <c>nonce</c> y la cookie
    /// de correlación de OIDC. Tampoco se exige a las peticiones con <c>Authorization: Bearer</c> (HU 1.3c): esa cabecera
    /// ya prueba lo mismo que la propia (un sitio ajeno no puede ponerla sin CORS) y el token no es una credencial que el
    /// navegador mande solo; esas peticiones solo valen en endpoints con <c>RequiereScope</c>, nunca con la cookie.
    /// </summary>
    public static IApplicationBuilder UseAntifalsificacionApi(this IApplicationBuilder app) =>
        app.Use(async (http, siguiente) =>
        {
            var peticion = http.Request;
            var cambia = !(HttpMethods.IsGet(peticion.Method) || HttpMethods.IsHead(peticion.Method) || HttpMethods.IsOptions(peticion.Method));
            if (cambia && peticion.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
                && !peticion.Path.Equals(RutaRetornoEntra, StringComparison.OrdinalIgnoreCase)
                && !peticion.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var origen = peticion.Headers.Origin.ToString();
                var mismoOrigen = string.IsNullOrEmpty(origen)
                    || string.Equals(origen, $"{peticion.Scheme}://{peticion.Host}", StringComparison.OrdinalIgnoreCase);
                if (!peticion.Headers.ContainsKey(CabeceraCsrf) || !mismoOrigen)
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await siguiente(http);
        });

    /// <summary>Solo con el proveedor simulado: el grupo de administración otorga atlas.superadmin (idempotente).</summary>
    private static async Task AsegurarGrupoAdministracionAsync(PlataformaDbContext plataforma, Guid grupo, CancellationToken ct)
    {
        var rol = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.Superadmin).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (rol is null || await plataforma.MapeosGrupoEntra.AnyAsync(m => m.GrupoId == grupo && m.RolId == rol, ct))
        {
            return;
        }
        plataforma.MapeosGrupoEntra.Add(new MapeoGrupoEntra { GrupoId = grupo, GrupoNombre = "Simulado: administración (solo local y dev)", RolId = rol.Value });
        await plataforma.SaveChangesAsync(ct);
    }

    /// <summary>Evita redirecciones abiertas: solo rutas locales de la PWA.</summary>
    internal static string RutaLocal(string? volver) =>
        !string.IsNullOrEmpty(volver) && volver.StartsWith('/') && !volver.StartsWith("//", StringComparison.Ordinal)
            && !volver.StartsWith("/\\", StringComparison.Ordinal)
            ? volver
            : "/";
}
