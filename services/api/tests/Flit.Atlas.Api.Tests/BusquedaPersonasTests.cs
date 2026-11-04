using System.Net;
using System.Net.Http.Json;
using Flit.Atlas.Identidad.Autorizacion;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Identidad.Autorizacion.BusquedaPersonas;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// Buscador de personas de «Asignación de roles» (<c>GET /api/v1/plataforma/personas</c>): búsqueda sin mayúsculas ni
/// tildes, filtros, tope, validación, permiso y aislamiento por organización.
/// </summary>
public class BusquedaPersonasTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string RutaPersonas = "/api/v1/plataforma/personas";

    private static string IpNueva() => $"2001:db8::41:{Random.Shared.Next(1, 0xffff):x}";

    /// <summary>Palabra única por prueba: la base es compartida y otras pruebas crean personas.</summary>
    private static string Marca() => "zq" + Guid.NewGuid().ToString("N")[..10];

    private sealed record Sembradas(Guid Interna, Guid Externa, Guid Jose, Guid Ana, Guid Maria);

    /// <summary>
    /// Dos empleados de la organización interna y una externa de un organismo nuevo, todos con la marca en el nombre o
    /// el correo. Ana tiene un rol vigente y José uno vencido.
    /// </summary>
    private async Task<Sembradas> SembrarAsync(string marca, string? baseDeDatos = null)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        // La organización interna la crea el primer ingreso de un empleado; en una base nueva puede no existir aún.
        var interna = await plataforma.Organizaciones.Where(o => o.Tipo == TipoOrganizacion.Interna).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(Ct);
        if (interna is null)
        {
            var nueva = new Organizacion { Nombre = Identidad.Aprovisionamiento.AprovisionadorEmpleados.NombreOrganizacionInterna, Tipo = TipoOrganizacion.Interna };
            plataforma.Organizaciones.Add(nueva);
            await plataforma.SaveChangesAsync(Ct);
            interna = nueva.Id;
        }
        var externa = new Organizacion { Nombre = $"Organismo Demo {marca}", Tipo = TipoOrganizacion.Cliente };
        var jose = new Persona { Tipo = TipoPersona.Empleado, OrganizacionId = interna.Value, Nombre = $"José {marca} Álvarez", Correo = $"jose.{marca}@ejemplo.com" };
        var ana = new Persona { Tipo = TipoPersona.Empleado, OrganizacionId = interna.Value, Nombre = $"Ana {marca} Pérez", Correo = $"ana.{marca}@ejemplo.com" };
        var maria = new Persona
        {
            Tipo = TipoPersona.Externo, OrganizacionId = externa.Id, Nombre = "María Demo", Correo = $"maria.{marca}@ejemplo.com",
            Estado = EstadoPersona.Suspendida,
        };
        var desarrollador = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.Desarrollador).Select(r => r.Id).SingleAsync(Ct);
        var soporte = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.Soporte).Select(r => r.Id).SingleAsync(Ct);
        plataforma.AddRange(externa, jose, ana, maria,
            new AsignacionRol { PersonaId = ana.Id, RolId = desarrollador, OrganizacionId = interna.Value, VigenteDesde = DateTimeOffset.UtcNow.AddDays(-1) },
            new AsignacionRol
            {
                PersonaId = jose.Id, RolId = soporte, OrganizacionId = interna.Value,
                VigenteDesde = DateTimeOffset.UtcNow.AddDays(-10), VigenteHasta = DateTimeOffset.UtcNow.AddDays(-1),
            });
        await plataforma.SaveChangesAsync(Ct);
        return new Sembradas(interna.Value, externa.Id, jose.Id, ana.Id, maria.Id);
    }

    private async Task<Navegador> EntrarConRolAsync(WebApplicationFactory<Program> api, string? rol, string? baseDeDatos = null)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        var grupos = rol is null ? [] : new[] { await entorno.GrupoConRolAsync(rol, baseDeDatos) };
        Assert.Equal(HttpStatusCode.NoContent,
            (await navegador.EntrarSimuladoAsync($"buscador.{Guid.NewGuid():N}@ejemplo.com", "Persona Demo", grupos)).StatusCode);
        return navegador;
    }

    private static async Task<ResultadoBusquedaPersonas> BuscarAsync(Navegador navegador, string consulta)
    {
        var respuesta = await navegador.GetAsync($"{RutaPersonas}?{consulta}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return (await respuesta.Content.ReadFromJsonAsync<ResultadoBusquedaPersonas>(Ct))!;
    }

    [Fact]
    public async Task Busca_por_nombre_o_correo_sin_mayusculas_ni_tildes_y_ordena_por_nombre()
    {
        await using var api = entorno.Api();
        var marca = Marca();
        var s = await SembrarAsync(marca);
        using var auditor = await EntrarConRolAsync(api, RolesAtlas.Auditor);

        var todas = await BuscarAsync(auditor, $"q={marca}");
        var jose = await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"JOSE {marca}")}");
        var perez = await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"{marca} pérez")}");
        var alvarez = await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"Álvarez {marca}")}");
        var porCorreo = await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"maria.{marca}@")}");
        var sinCoincidencia = await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"{marca} gómez")}");

        Assert.Equal([s.Ana, s.Jose, s.Maria], todas.Personas.Select(p => p.Id));
        Assert.False(todas.HayMas);
        Assert.Equal([s.Jose], jose.Personas.Select(p => p.Id));
        Assert.Equal([s.Ana], perez.Personas.Select(p => p.Id));
        Assert.Equal([s.Jose], alvarez.Personas.Select(p => p.Id));
        Assert.Equal([s.Maria], porCorreo.Personas.Select(p => p.Id));
        Assert.Empty(sinCoincidencia.Personas);

        var ana = todas.Personas[0];
        Assert.Equal(("empleado", "activa", false), (ana.Tipo, ana.Estado, ana.Emergencia));
        Assert.Equal(s.Interna, ana.Organizacion.Id);
        Assert.Equal([RolesAtlas.Desarrollador], ana.Roles);
        // El rol vencido de José no cuenta.
        Assert.Empty(todas.Personas[1].Roles);
        var maria = todas.Personas[2];
        Assert.Equal(("externo", "suspendida", $"maria.{marca}@ejemplo.com"), (maria.Tipo, maria.Estado, maria.Correo));
        Assert.Equal(new OrganizacionResumen(s.Externa, $"Organismo Demo {marca}"), maria.Organizacion);
    }

    [Fact]
    public async Task Filtra_por_tipo_y_organizacion_busca_por_id_y_respeta_el_tope()
    {
        await using var api = entorno.Api();
        var marca = Marca();
        var s = await SembrarAsync(marca);
        using var lider = await EntrarConRolAsync(api, RolesAtlas.LiderTecnico);

        var externos = await BuscarAsync(lider, $"q={marca}&tipo=externo");
        var empleados = await BuscarAsync(lider, $"q={marca}&tipo=EMPLEADO");
        var deLaExterna = await BuscarAsync(lider, $"q={marca}&organizacion={s.Externa}");
        var porId = await BuscarAsync(lider, $"q={s.Jose}");
        var tope = await BuscarAsync(lider, $"q={marca}&limite=2");

        Assert.Equal([s.Maria], externos.Personas.Select(p => p.Id));
        Assert.Equal([s.Ana, s.Jose], empleados.Personas.Select(p => p.Id));
        Assert.Equal([s.Maria], deLaExterna.Personas.Select(p => p.Id));
        Assert.Equal([s.Jose], porId.Personas.Select(p => p.Id));
        Assert.Equal([s.Ana, s.Jose], tope.Personas.Select(p => p.Id));
        Assert.True(tope.HayMas);
    }

    [Fact]
    public async Task Los_comodines_de_like_se_buscan_como_texto()
    {
        await using var api = entorno.Api();
        var marca = Marca();
        await SembrarAsync(marca);
        using var auditor = await EntrarConRolAsync(api, RolesAtlas.Auditor);

        Assert.Empty((await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"{marca} %")}")).Personas);
        Assert.Empty((await BuscarAsync(auditor, $"q={Uri.EscapeDataString($"{marca} __")}")).Personas);
    }

    [Theory]
    [InlineData("q=a")]
    [InlineData("q=%20a%20")]
    [InlineData("")]
    [InlineData("q=ana&tipo=otro")]
    [InlineData("q=ana&limite=0")]
    [InlineData("q=ana&limite=51")]
    public async Task Valida_el_minimo_de_caracteres_el_tipo_y_el_limite(string consulta)
    {
        await using var api = entorno.Api();
        using var auditor = await EntrarConRolAsync(api, RolesAtlas.Auditor);

        var respuesta = await auditor.GetAsync($"{RutaPersonas}?{consulta}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Rechaza_una_busqueda_de_mas_de_100_caracteres()
    {
        await using var api = entorno.Api();
        using var auditor = await EntrarConRolAsync(api, RolesAtlas.Auditor);

        var respuesta = await auditor.GetAsync($"{RutaPersonas}?q={new string('a', LargoMaximo + 1)}");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_responde_401_y_sin_atlas_roles_ver_403()
    {
        await using var api = entorno.Api();
        using var anonimo = Navegador.De(api);
        anonimo.Ip = IpNueva();
        using var lector = await EntrarConRolAsync(api, null);
        using var desarrollador = await EntrarConRolAsync(api, RolesAtlas.Desarrollador);
        var admin = await entorno.ExternoAsync(api, RolesAtlas.AdminOrganizacion, DateTimeOffset.UtcNow.AddDays(30));
        using var adminOrganizacion = Navegador.De(api);
        adminOrganizacion.Ip = IpNueva();
        Assert.Equal(HttpStatusCode.NoContent, (await adminOrganizacion.EntrarComoAsync(admin.UsuarioId)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"{RutaPersonas}?q=ana")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lector.GetAsync($"{RutaPersonas}?q=ana")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await desarrollador.GetAsync($"{RutaPersonas}?q=ana")).StatusCode);
        // El administrador de una organización externa invita, pero no ve personas (§7.3).
        Assert.Equal(HttpStatusCode.Forbidden, (await adminOrganizacion.GetAsync($"{RutaPersonas}?q=ana")).StatusCode);
    }

    [Fact]
    public async Task Con_el_alcance_de_un_externo_solo_aparecen_personas_de_su_organizacion()
    {
        var marca = Marca();
        var s = await SembrarAsync(marca);
        var ahora = DateTimeOffset.UtcNow;

        await using var interno = Contexto(AlcanceDatos.Interno);
        await using var externo = Contexto(AlcanceDatos.DeOrganizaciones(s.Externa));
        await using var otraOrganizacion = Contexto(AlcanceDatos.DeOrganizaciones(Guid.NewGuid()));
        var paraInterno = await ConsultarAsync(interno, marca, null, null, LimiteMaximo, ahora, Ct);
        var paraExterno = await ConsultarAsync(externo, marca, null, null, LimiteMaximo, ahora, Ct);
        var porIdAjeno = await ConsultarAsync(externo, s.Ana.ToString(), null, null, LimiteMaximo, ahora, Ct);
        var filtrandoOtra = await ConsultarAsync(externo, marca, null, s.Interna, LimiteMaximo, ahora, Ct);
        var paraOtra = await ConsultarAsync(otraOrganizacion, marca, null, null, LimiteMaximo, ahora, Ct);

        Assert.Equal([s.Ana, s.Jose, s.Maria], paraInterno.Personas.Select(p => p.Id));
        Assert.Equal([s.Maria], paraExterno.Personas.Select(p => p.Id));
        Assert.Empty(porIdAjeno.Personas);
        Assert.Empty(filtrandoOtra.Personas);
        Assert.Empty(paraOtra.Personas);
    }

    [Fact]
    public async Task Marca_la_cuenta_de_emergencia()
    {
        // Base propia: la cuenta de emergencia es una sola por ambiente y EmergenciaTests usa la compartida.
        var baseDeDatos = await entorno.BaseNuevaAsync();
        await using var api = entorno.Api(baseDeDatos: baseDeDatos);
        var marca = Marca();
        var s = await SembrarAsync(marca, baseDeDatos);
        await using (var plataforma = entorno.Plataforma(baseDeDatos))
        {
            var superadmin = await plataforma.Roles.Where(r => r.Codigo == RolesAtlas.Superadmin).Select(r => r.Id).SingleAsync(Ct);
            plataforma.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = s.Jose, RolId = superadmin, OrganizacionId = s.Interna, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
                Origen = OrigenAsignacion.Emergencia,
            });
            await plataforma.SaveChangesAsync(Ct);
        }
        using var auditor = await EntrarConRolAsync(api, RolesAtlas.Auditor, baseDeDatos);

        var resultado = await BuscarAsync(auditor, $"q={marca}");

        Assert.Equal([false, true, false], resultado.Personas.Select(p => p.Emergencia));
        Assert.Equal([RolesAtlas.Superadmin], resultado.Personas[1].Roles);
    }

    private PlataformaDbContext Contexto(AlcanceDatos alcance)
    {
        var opciones = new DbContextOptionsBuilder<PlataformaDbContext>();
        PlataformaDbContext.Configurar(opciones, entorno.Postgres.GetConnectionString());
        return new PlataformaDbContext(opciones.Options, new AlcanceFijo(alcance));
    }

    private sealed class AlcanceFijo(AlcanceDatos alcance) : IAlcanceDatos
    {
        public AlcanceDatos Actual => alcance;
    }
}
