using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>
/// HU 1.4 (prompt maestro §12, ATLAS-ADR-0023): un usuario de la organización A no ve filas de la organización B. Se
/// prueba cada capa por separado (predicado obligatorio de EF Core y RLS de PostgreSQL) con el usuario del servicio
/// <c>flit_atlas</c>, que es el dueño de las tablas: por eso RLS va con <c>FORCE</c>.
/// </summary>
public class AislamientoTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Escenario(BaseDePrueba Bd, Guid OrgA, Guid OrgB, Guid PersonaA, Guid PersonaB);

    /// <summary>Dos organizaciones cliente con una persona, una asignación, una invitación y un evento de auditoría cada una.</summary>
    private async Task<Escenario> EscenarioAsync()
    {
        var bd = await contenedores.CrearBaseAsync();
        await using var sistema = bd.Contexto();
        await SembradorPermisos.SembrarAsync(sistema, CatalogoPermisos.Crear(ManifiestosNucleo.Todos), Ct);
        var rol = await sistema.Roles.SingleAsync(r => r.Codigo == RolesAtlas.Integrador, Ct);
        var ids = new List<(Guid Org, Guid Persona)>();
        foreach (var nombre in new[] { "Organismo Demo A", "Organismo Demo B" })
        {
            var organizacion = new Organizacion { Nombre = nombre, Tipo = TipoOrganizacion.Cliente };
            var persona = new Persona
            {
                Tipo = TipoPersona.Externo,
                OrganizacionId = organizacion.Id,
                Nombre = $"Persona de {nombre}",
                Correo = $"persona.{Guid.NewGuid():N}@ejemplo.com",
            };
            sistema.AddRange(organizacion, persona);
            sistema.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = persona.Id,
                RolId = rol.Id,
                OrganizacionId = organizacion.Id,
                VigenteDesde = DateTimeOffset.UtcNow,
                VigenteHasta = DateTimeOffset.UtcNow.AddDays(30),
            });
            sistema.Invitaciones.Add(new Invitacion
            {
                OrganizacionId = organizacion.Id,
                Correo = $"invitada.{Guid.NewGuid():N}@ejemplo.com",
                RolId = rol.Id,
                Alcance = "{}",
                TokenHash = Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray(),
                TokenVenceEn = DateTimeOffset.UtcNow.AddHours(72),
                InvitadoPor = persona.Id,
            });
            sistema.Auditoria.Add(new EventoAuditoria
            {
                OcurridoEn = DateTimeOffset.UtcNow,
                ActorTipo = TipoActor.Sistema,
                OrganizacionId = organizacion.Id,
                Accion = "plataforma.prueba",
                RecursoTipo = "prueba",
            });
            ids.Add((organizacion.Id, persona.Id));
        }
        sistema.Auditoria.Add(new EventoAuditoria
        {
            OcurridoEn = DateTimeOffset.UtcNow, ActorTipo = TipoActor.Sistema, Accion = "plataforma.prueba", RecursoTipo = "prueba",
        });
        await sistema.SaveChangesAsync(Ct);
        return new Escenario(bd, ids[0].Org, ids[1].Org, ids[0].Persona, ids[1].Persona);
    }

    [Fact]
    public async Task Con_el_predicado_un_externo_de_A_solo_ve_filas_de_A()
    {
        var e = await EscenarioAsync();
        await using var deA = e.Bd.Contexto(AlcanceDatos.DeOrganizaciones(e.OrgA));

        Assert.Equal([e.OrgA], await deA.Organizaciones.Select(o => o.Id).ToListAsync(Ct));
        Assert.Equal([e.PersonaA], await deA.Personas.Select(p => p.Id).ToListAsync(Ct));
        Assert.Equal([e.PersonaA], await deA.AsignacionesRol.Select(a => a.PersonaId).ToListAsync(Ct));
        Assert.Equal([e.OrgA], await deA.Invitaciones.Select(i => i.OrganizacionId).ToListAsync(Ct));
        // La auditoría sin organización tampoco: solo la ve un alcance total.
        Assert.Equal(new Guid?[] { e.OrgA }, await deA.Auditoria.Select(a => a.OrganizacionId).ToListAsync(Ct));
        Assert.Null(await deA.Personas.FirstOrDefaultAsync(p => p.Id == e.PersonaB, Ct));
    }

    [Fact]
    public async Task Sin_el_predicado_RLS_sigue_ocultando_las_filas_de_B()
    {
        var e = await EscenarioAsync();
        await using var deA = e.Bd.Contexto(AlcanceDatos.DeOrganizaciones(e.OrgA));

        // IgnoreQueryFilters y SQL directo se saltan la primera capa; RLS (segunda capa) filtra igual.
        Assert.Equal([e.PersonaA], await deA.Personas.IgnoreQueryFilters().Select(p => p.Id).ToListAsync(Ct));
        Assert.Equal([e.OrgA], await deA.Database.SqlQueryRaw<Guid>("SELECT id AS \"Value\" FROM atlas.plataforma_organizacion").ToListAsync(Ct));
        Assert.Equal(1, await deA.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM atlas.plataforma_asignacion_rol").SingleAsync(Ct));
        Assert.Equal(1, await deA.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM atlas.plataforma_invitacion").SingleAsync(Ct));
        Assert.Equal(1, await deA.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM atlas.plataforma_auditoria").SingleAsync(Ct));
        // Tampoco puede cambiar ni borrar lo de B: para RLS esas filas no existen.
        Assert.Equal(0, await deA.Personas.IgnoreQueryFilters().Where(p => p.Id == e.PersonaB)
            .ExecuteUpdateAsync(p => p.SetProperty(x => x.Nombre, "Alterada"), Ct));
        Assert.Equal(0, await deA.AsignacionesRol.IgnoreQueryFilters().Where(a => a.PersonaId == e.PersonaB).ExecuteDeleteAsync(Ct));
    }

    [Fact]
    public async Task RLS_impide_escribir_filas_de_otra_organizacion()
    {
        var e = await EscenarioAsync();
        await using var deA = e.Bd.Contexto(AlcanceDatos.DeOrganizaciones(e.OrgA));
        deA.Personas.Add(new Persona
        {
            Tipo = TipoPersona.Externo, OrganizacionId = e.OrgB, Nombre = "Intrusa", Correo = $"intrusa.{Guid.NewGuid():N}@ejemplo.com",
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => deA.SaveChangesAsync(Ct));

        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
        Assert.Contains("row-level security", postgres.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_conexion_sin_alcance_no_ve_ninguna_fila()
    {
        var e = await EscenarioAsync();

        // Fuera de la app (sin el interceptor) el usuario del servicio no ve nada: RLS niega por defecto.
        Assert.Equal(["0"], await MigracionesTests.ConsultarAsync(e.Bd.Servicio, "SELECT count(*)::text FROM atlas.plataforma_persona"));
        Assert.Equal(["0"], await MigracionesTests.ConsultarAsync(e.Bd.Servicio, "SELECT count(*)::text FROM atlas.plataforma_organizacion"));
        await using var ninguno = e.Bd.Contexto(AlcanceDatos.Ninguno);
        Assert.Empty(await ninguno.Personas.IgnoreQueryFilters().ToListAsync(Ct));
    }

    [Fact]
    public async Task El_sistema_y_los_empleados_ven_todas_las_organizaciones()
    {
        var e = await EscenarioAsync();
        await using var sistema = e.Bd.Contexto();
        await using var interno = e.Bd.Contexto(AlcanceDatos.Interno);

        Assert.Equal(2, await sistema.Personas.CountAsync(Ct));
        Assert.Equal(2, await interno.Personas.CountAsync(Ct));
        Assert.Equal(3, await interno.Auditoria.CountAsync(Ct));
    }

    [Fact]
    public async Task Toda_tabla_con_organizacion_id_tiene_RLS_forzada_y_una_politica()
    {
        var bd = await contenedores.CrearBaseAsync();

        var sinRls = await MigracionesTests.ConsultarAsync(bd.Admin, """
            SELECT c.relname FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            WHERE c.relkind = 'r'
              AND (EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.oid AND a.attname = 'organizacion_id' AND NOT a.attisdropped)
                   OR c.relname = 'plataforma_organizacion')
              AND (NOT c.relrowsecurity OR NOT c.relforcerowsecurity
                   OR NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid))
            """);
        var conRls = await MigracionesTests.ConsultarAsync(bd.Admin, """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            WHERE c.relrowsecurity AND c.relforcerowsecurity ORDER BY c.relname
            """);

        Assert.Empty(sinRls);
        Assert.Equal(
            ["plataforma_asignacion_rol", "plataforma_auditoria", "plataforma_invitacion", "plataforma_organizacion", "plataforma_persona"],
            conRls);
    }
}
