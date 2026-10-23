using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>HU 1.4 (ATLAS-ADR-0023): manifiestos, matriz rol × permiso, sembrado y permisos efectivos con vigencia.</summary>
public class PermisosTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CatalogoPermisos Catalogo() => CatalogoPermisos.Crear(ManifiestosNucleo.Todos);

    /// <summary>Reloj que la prueba adelanta a mano.</summary>
    private sealed class Reloj(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class Implicitos(params RolImplicito[] roles) : IProveedorRolesImplicitos
    {
        public Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RolImplicito>>(roles);
    }

    private sealed class SinImplicitos : IProveedorRolesImplicitos
    {
        public Task<IReadOnlyList<RolImplicito>> ObtenerAsync(Guid personaId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RolImplicito>>([]);
    }

    [Fact]
    public void El_catalogo_del_nucleo_tiene_los_roles_del_7_y_el_superadmin_recibe_todos_los_permisos()
    {
        var catalogo = Catalogo();

        Assert.Equal(14, catalogo.Roles.Count);
        Assert.Equal(catalogo.Permisos.Select(p => p.Permiso.Codigo).Order(StringComparer.Ordinal),
            catalogo.PermisosDe(RolesAtlas.Superadmin).Order(StringComparer.Ordinal));
        Assert.Contains("atlas.roles.gestionar", catalogo.PermisosDe(RolesAtlas.Superadmin));
        Assert.DoesNotContain("atlas.roles.gestionar", catalogo.PermisosDe(RolesAtlas.LiderTecnico));
        Assert.All(RolesAtlas.Externos, r => Assert.DoesNotContain(catalogo.PermisosDe(r),
            p => p.StartsWith("atlas.auditoria", StringComparison.Ordinal) || p.StartsWith("atlas.gia", StringComparison.Ordinal)
                || p.StartsWith("atlas.equipo", StringComparison.Ordinal) || p.StartsWith("atlas.soporte", StringComparison.Ordinal)
                || p.StartsWith("atlas.arquitectura.ver_despliegue", StringComparison.Ordinal)));
        // La lista base del §7.4 (más atlas.roles.ver de la HU 1.4), sin el catálogo ni los 4 de soporte: los publican
        // sus módulos (ATLAS-ADR-0028 y HU 2.6).
        Assert.Equal(28, catalogo.Permisos.Count);
        Assert.Contains(PermisosPlataforma.RolesVer, catalogo.PermisosDe(RolesAtlas.Auditor));
    }

    [Fact]
    public void Un_permiso_en_dos_manifiestos_falla_al_armar_el_catalogo()
    {
        var otro = new ManifiestoPermisos("otro", "Otro", [new PermisoAtlas(PermisosPlataforma.RolesVer, "Repetido.")]);

        var error = Assert.Throws<InvalidOperationException>(() => CatalogoPermisos.Crear([PermisosPlataforma.Manifiesto, otro]));

        Assert.Contains(PermisosPlataforma.RolesVer, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ext.inventado")]
    [InlineData(RolesAtlas.Superadmin)]
    public void Conceder_a_un_rol_inexistente_o_al_superadmin_falla(string rol)
    {
        var manifiesto = new ManifiestoPermisos("hola", "Hola", [new PermisoAtlas("atlas.hola.ver", "Ver hola.", rol)]);

        Assert.Throws<InvalidOperationException>(() => CatalogoPermisos.Crear([manifiesto]));
    }

    [Theory]
    [InlineData("hola.ver")]
    [InlineData("atlas.hola")]
    [InlineData("atlas.Hola.ver")]
    [InlineData("atlas.hola.ver-todo")]
    public void Un_permiso_fuera_del_formato_atlas_modulo_accion_falla(string codigo) =>
        Assert.Throws<ArgumentException>(() => new PermisoAtlas(codigo, "Descripción."));

    [Fact]
    public void Un_modulo_nuevo_suma_sus_permisos_y_sus_tipos_de_alcance_sin_tocar_los_demas()
    {
        var hola = new ManifiestoPermisos("hola", "Hola",
            [new PermisoAtlas("atlas.hola.ver", "Ver hola.", RolesAtlas.Lector, RolesAtlas.Integrador)], ["saludo"]);

        var catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, hola]);

        Assert.Contains("atlas.hola.ver", catalogo.PermisosDe(RolesAtlas.Lector));
        Assert.Contains("atlas.hola.ver", catalogo.PermisosDe(RolesAtlas.Superadmin));
        Assert.Contains("saludo", catalogo.TiposAlcance);
        Assert.Equal("hola", catalogo.Matriz().Modulos[^1].Id);
        Assert.Contains("| `atlas.hola.ver` |", catalogo.MatrizEnMarkdown(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_sembrado_es_idempotente_y_deja_la_base_igual_al_codigo()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        await using var contexto = bd.Contexto();

        var primero = await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);
        var segundo = await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);

        Assert.Equal(14, primero.RolesCreados);
        Assert.True(segundo.SinCambios, segundo.ToString());
        var roles = await contexto.Roles.ToDictionaryAsync(r => r.Id, r => r.Codigo, Ct);
        var enBase = (await contexto.RolesPermisos.ToListAsync(Ct)).Select(rp => $"{roles[rp.RolId]} {rp.PermisoCodigo}").Order(StringComparer.Ordinal);
        var enCodigo = catalogo.Roles.SelectMany(r => catalogo.PermisosDe(r.Codigo).Select(p => $"{r.Codigo} {p}")).Order(StringComparer.Ordinal);
        Assert.Equal(enCodigo, enBase);
        Assert.Equal(catalogo.Permisos.Count, await contexto.Permisos.CountAsync(Ct));
    }

    [Fact]
    public async Task El_sembrado_retira_lo_que_el_codigo_ya_no_concede()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var contexto = bd.Contexto();
        var hola = new ManifiestoPermisos("hola", "Hola", [new PermisoAtlas("atlas.hola.ver", "Ver hola.", RolesAtlas.Lector)]);
        await SembradorPermisos.SembrarAsync(contexto, CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, hola]), Ct);

        var resultado = await SembradorPermisos.SembrarAsync(contexto, Catalogo(), Ct);

        Assert.Equal(1, resultado.PermisosRetirados);
        Assert.False(await contexto.RolesPermisos.AnyAsync(rp => rp.PermisoCodigo == "atlas.hola.ver", Ct));
    }

    /// <summary>Persona activa de una organización nueva, con un rol asignado entre <paramref name="desde"/> y <paramref name="hasta"/>.</summary>
    private static async Task<(Guid Persona, Guid Organizacion)> PersonaConRolAsync(
        PlataformaDbContext contexto, TipoPersona tipo, string rol, DateTimeOffset desde, DateTimeOffset? hasta)
    {
        var organizacion = new Organizacion
        {
            Nombre = $"Organismo Demo {Guid.NewGuid():N}", Tipo = tipo == TipoPersona.Empleado ? TipoOrganizacion.Interna : TipoOrganizacion.Cliente,
        };
        var persona = new Persona
        {
            Tipo = tipo, OrganizacionId = organizacion.Id, Nombre = "Persona Demo", Correo = $"persona.{Guid.NewGuid():N}@ejemplo.com",
        };
        var rolId = await contexto.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync(Ct);
        contexto.AddRange(organizacion, persona, new AsignacionRol
        {
            PersonaId = persona.Id, RolId = rolId, OrganizacionId = organizacion.Id, VigenteDesde = desde, VigenteHasta = hasta,
        });
        await contexto.SaveChangesAsync(Ct);
        return (persona.Id, organizacion.Id);
    }

    [Fact]
    public async Task Un_rol_externo_vencido_pierde_sus_permisos_sin_intervencion()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        var reloj = new Reloj(DateTimeOffset.UtcNow);
        await using var sistema = bd.Contexto();
        await SembradorPermisos.SembrarAsync(sistema, catalogo, Ct);
        var (persona, organizacion) = await PersonaConRolAsync(sistema, TipoPersona.Externo, RolesAtlas.Proveedor,
            reloj.Ahora.AddMinutes(-5), reloj.Ahora.AddHours(1));
        // Como la consulta de una petición del externo: con el alcance de su organización (predicado y RLS).
        await using var suyo = bd.Contexto(AlcanceDatos.DeOrganizaciones(organizacion));
        var calculadora = new CalculadoraPermisos(suyo, catalogo, new SinImplicitos(), reloj);

        var vigente = await calculadora.CalcularAsync(persona, Ct);
        reloj.Ahora = reloj.Ahora.AddHours(1);
        var vencido = await calculadora.CalcularAsync(persona, Ct);

        Assert.Equal([RolesAtlas.Proveedor], vigente.Roles);
        Assert.True(vigente.Tiene("atlas.portal.ver"));
        Assert.Empty(vencido.Roles);
        Assert.Empty(vencido.Permisos);
        Assert.False(vencido.Tiene("atlas.portal.ver"));
    }

    [Fact]
    public async Task Una_asignacion_futura_no_cuenta_hasta_que_empieza()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        var reloj = new Reloj(DateTimeOffset.UtcNow);
        await using var contexto = bd.Contexto();
        await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);
        var (persona, _) = await PersonaConRolAsync(contexto, TipoPersona.Empleado, RolesAtlas.Auditor, reloj.Ahora.AddDays(1), null);
        var calculadora = new CalculadoraPermisos(contexto, catalogo, new SinImplicitos(), reloj);

        var antes = await calculadora.CalcularAsync(persona, Ct);
        reloj.Ahora = reloj.Ahora.AddDays(2);
        var despues = await calculadora.CalcularAsync(persona, Ct);

        // Todo empleado tiene atlas.lector.
        Assert.Equal([RolesAtlas.Lector], antes.Roles);
        Assert.False(antes.Tiene(PermisosPlataforma.AuditoriaVer));
        Assert.Equal([RolesAtlas.Auditor, RolesAtlas.Lector], despues.Roles);
        Assert.True(despues.Tiene(PermisosPlataforma.AuditoriaVer));
    }

    [Fact]
    public async Task Una_persona_suspendida_o_de_una_organizacion_suspendida_no_tiene_permisos()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        await using var contexto = bd.Contexto();
        await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);
        var (suspendida, _) = await PersonaConRolAsync(contexto, TipoPersona.Empleado, RolesAtlas.Auditor, DateTimeOffset.UtcNow.AddMinutes(-1), null);
        var (deOrganizacionSuspendida, organizacion) = await PersonaConRolAsync(contexto, TipoPersona.Externo, RolesAtlas.Integrador,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        await contexto.Personas.Where(p => p.Id == suspendida).ExecuteUpdateAsync(p => p.SetProperty(x => x.Estado, EstadoPersona.Suspendida), Ct);
        await contexto.Organizaciones.Where(o => o.Id == organizacion).ExecuteUpdateAsync(o => o.SetProperty(x => x.Estado, EstadoOrganizacion.Suspendida), Ct);
        var calculadora = new CalculadoraPermisos(contexto, catalogo, new SinImplicitos(), TimeProvider.System);

        Assert.Empty((await calculadora.CalcularAsync(suspendida, Ct)).Permisos);
        Assert.Empty((await calculadora.CalcularAsync(deOrganizacionSuspendida, Ct)).Permisos);
    }

    [Fact]
    public async Task Un_rol_externo_asignado_a_un_empleado_no_concede_nada()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        await using var contexto = bd.Contexto();
        await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);
        var (persona, _) = await PersonaConRolAsync(contexto, TipoPersona.Empleado, RolesAtlas.Integrador, DateTimeOffset.UtcNow.AddMinutes(-1), null);

        var permisos = await new CalculadoraPermisos(contexto, catalogo, new SinImplicitos(), TimeProvider.System).CalcularAsync(persona, Ct);

        Assert.Equal([RolesAtlas.Lector], permisos.Roles);
    }

    [Fact]
    public async Task El_rol_implicito_suma_permisos_solo_en_el_alcance_de_su_recurso()
    {
        var bd = await contenedores.CrearBaseAsync();
        var catalogo = Catalogo();
        await using var contexto = bd.Contexto();
        await SembradorPermisos.SembrarAsync(contexto, catalogo, Ct);
        var (persona, _) = await PersonaConRolAsync(contexto, TipoPersona.Empleado, RolesAtlas.Negocio, DateTimeOffset.UtcNow.AddMinutes(-1), null);
        var flito = new AlcanceAsignacion(TiposAlcance.Aplicativo, "flito");
        var proveedor = new Implicitos(new RolImplicito(RolesAtlas.Responsable, flito), new RolImplicito(RolesAtlas.Superadmin, flito));

        var permisos = await new CalculadoraPermisos(contexto, catalogo, proveedor, TimeProvider.System).CalcularAsync(persona, Ct);

        // El proveedor no puede colar un rol que no sea implícito.
        Assert.DoesNotContain(RolesAtlas.Superadmin, permisos.Roles);
        Assert.True(permisos.Tiene("atlas.docs.aprobar"));
        Assert.True(permisos.TieneEn("atlas.docs.aprobar", flito));
        Assert.False(permisos.TieneEn("atlas.docs.aprobar", new AlcanceAsignacion(TiposAlcance.Aplicativo, "vigia")));
        // Lo que da un rol global vale en cualquier recurso.
        Assert.True(permisos.TieneEn("atlas.docs.exportar", new AlcanceAsignacion(TiposAlcance.Aplicativo, "vigia")));
    }
}
