using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Contratos.Comparticiones;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Paquetes;
using Flit.Atlas.Modulos.Contratos.Portal;
using Flit.Atlas.Plataforma;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Modulos;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Atlas.Modulos.Contratos.Tests;

/// <summary>
/// HU 4.3 y 4.4 (ATLAS-ADR-0043) contra la base con el usuario del servicio <c>flit_atlas</c> (RLS no se salta): la tabla
/// de comparticiones tiene RLS forzada; el predicado y RLS ocultan las comparticiones de otra organización; el acceso del
/// portal solo llega a paquetes de la organización de la sesión, y RLS lo ataja aunque el código se equivocara de
/// organización (defensa en profundidad); y las reglas puras del alcance de los roles, la vigencia y los cambios.
/// </summary>
public class PortalTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CatalogoPermisos Catalogo = CatalogoPermisos.Crear([.. ManifiestosNucleo.Todos, PermisosContratos.Manifiesto]);

    private const string Spec = """
        openapi: 3.1.0
        info:
          title: API Demo Portal
          version: 1.0.0
          x-flit-visibilidad: compartible
        paths:
          /tramites/{id}:
            get:
              operationId: obtenerTramite
              responses:
                '200': { description: El trámite. }
          /pagos-internos:
            post:
              operationId: pagarInterno
              responses:
                '200': { description: Pagado. }
        """;

    private sealed record Escenario(BaseDePrueba Bd, Guid OrgA, Guid OrgB, Guid PaqueteA, Guid PaqueteB, Guid ContratoA);

    /// <summary>Dos organizaciones, un paquete publicado compartido con cada una (A con exportar), escritos por el sistema.</summary>
    private async Task<Escenario> EscenarioAsync()
    {
        var bd = await contenedores.CrearBaseAsync();
        var a = new Organizacion { Nombre = "Organismo Demo A", Tipo = TipoOrganizacion.Cliente };
        var b = new Organizacion { Nombre = "Organismo Demo B", Tipo = TipoOrganizacion.Cliente };
        await using (var plataforma = bd.Plataforma())
        {
            plataforma.AddRange(a, b);
            await plataforma.SaveChangesAsync(Ct);
        }
        await using var sistema = bd.Contratos();
        var (contratoA, versionA) = Contrato(sistema, "api-demo-a");
        var (contratoB, versionB) = Contrato(sistema, "api-demo-b");
        var paqueteA = Paquete(sistema, "Paquete Demo A", contratoA, versionA);
        var paqueteB = Paquete(sistema, "Paquete Demo B", contratoB, versionB);
        await sistema.SaveChangesAsync(Ct);
        sistema.Comparticiones.AddRange(
            new ComparticionPaquete { PaqueteId = paqueteA, OrganizacionId = a.Id, PuedeExportar = true, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-5), OtorgadoPor = Guid.NewGuid() },
            new ComparticionPaquete { PaqueteId = paqueteB, OrganizacionId = b.Id, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-5), OtorgadoPor = Guid.NewGuid() });
        await sistema.SaveChangesAsync(Ct);
        return new Escenario(bd, a.Id, b.Id, paqueteA, paqueteB, contratoA);
    }

    private static (Guid Contrato, Guid Version) Contrato(ContratosDbContext contexto, string codigo)
    {
        var contrato = new Contrato
        {
            AplicativoId = Guid.NewGuid(), Codigo = codigo, Tipo = TipoContrato.Openapi, Nombre = $"API {codigo}", Repositorio = "flitsas-demo/api-demo",
            Visibilidad = VisibilidadContrato.Compartible,
        };
        var version = new VersionContrato
        {
            ContratoId = contrato.Id, Version = "1.0.0", Mayor = 1, Menor = 0, Parche = 0, EsEstable = true,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Spec))), Formato = FormatoSpec.Yaml, Contenido = Spec,
            Tamano = Encoding.UTF8.GetByteCount(Spec), Repositorio = "flitsas-demo/api-demo", Rama = "release", CommitSha = new string('f', 40),
            WorkflowDelJob = "flitsas/flit-atlas/.github/workflows/atlas-publicar.yml@refs/tags/v1", PublicadoPor = "github:flitsas-demo/api-demo@release",
            PublicadoEn = DateTimeOffset.UtcNow,
        };
        contexto.Contratos.Add(contrato);
        contexto.Versiones.Add(version);
        contexto.Operaciones.AddRange(
            new OperacionContrato { VersionId = version.Id, Clave = "GET /tramites/{id}", Clase = "operacion", Metodo = "GET", Ruta = "/tramites/{id}" },
            new OperacionContrato { VersionId = version.Id, Clave = "POST /pagos-internos", Clase = "operacion", Metodo = "POST", Ruta = "/pagos-internos" });
        return (contrato.Id, version.Id);
    }

    private static Guid Paquete(ContratosDbContext contexto, string nombre, Guid contrato, Guid version)
    {
        var paquete = new PaqueteContratos
        {
            Nombre = nombre, Estado = EstadoPaquete.Publicado, RevisionActual = 1, RevisionPublicada = 1, PublicadaEn = DateTimeOffset.UtcNow, CreadoPor = Guid.NewGuid(),
        };
        var foto = new FotoPaquete(nombre, null, [new EntradaPaquete(contrato, version, ["GET /tramites/{id}"], [])]);
        contexto.Paquetes.Add(paquete);
        contexto.RevisionesPaquete.Add(new RevisionPaquete
        {
            PaqueteId = paquete.Id, Numero = 1, Nombre = nombre, Definicion = foto.DefinicionJson(), Nota = "Primera.", AutorPersonaId = Guid.NewGuid(),
            GuardadaEn = DateTimeOffset.UtcNow, Huella = foto.Huella(),
        });
        return paquete.Id;
    }

    [Fact]
    public async Task La_tabla_de_comparticiones_tiene_RLS_forzada_y_su_politica()
    {
        var bd = await contenedores.CrearBaseAsync();

        var rls = await Contenedores.ConsultarAsync(bd.Admin, """
            SELECT c.relname || ':' || c.relrowsecurity || ':' || c.relforcerowsecurity || ':' || p.polname
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'atlas'
            JOIN pg_policy p ON p.polrelid = c.oid
            WHERE c.relname = 'contratos_comparticion'
            """);

        Assert.Equal(["contratos_comparticion:true:true:organizacion_visible"], rls);
    }

    [Fact]
    public async Task Con_el_predicado_y_sin_el_un_externo_de_A_solo_ve_las_comparticiones_de_A()
    {
        var e = await EscenarioAsync();
        await using var externoA = e.Bd.Contratos(AlcanceDatos.DeOrganizaciones(e.OrgA));

        Assert.Equal([e.PaqueteA], await externoA.Comparticiones.Select(c => c.PaqueteId).ToListAsync(Ct));
        // Sin el predicado (un error en el código), RLS sigue ocultando las de B.
        Assert.Equal([e.OrgA], await externoA.Comparticiones.IgnoreQueryFilters().Select(c => c.OrganizacionId).ToListAsync(Ct));
        await using var ninguno = e.Bd.Contratos(AlcanceDatos.Ninguno);
        Assert.Equal(0, await ninguno.Comparticiones.IgnoreQueryFilters().CountAsync(Ct));
    }

    [Fact]
    public async Task En_SQL_con_las_variables_de_RLS_la_consulta_del_portal_solo_trae_paquetes_de_la_organizacion()
    {
        var e = await EscenarioAsync();
        // La misma forma de la consulta del portal (compartición → paquete → revisión), en SQL directo con el usuario del
        // servicio y las variables que fija el interceptor.
        string Consulta(string organizaciones) => $"""
            SELECT set_config('atlas.alcance_total', 'off', false), set_config('atlas.organizaciones', '{organizaciones}', false);
            SELECT p.id::text FROM atlas.contratos_comparticion c
            JOIN atlas.contratos_paquete p ON p.id = c.paquete_id
            JOIN atlas.contratos_paquete_revision r ON r.paquete_id = p.id AND r.numero = coalesce(c.revision_fijada, p.revision_publicada)
            ORDER BY p.id
            """;

        Assert.Equal([e.PaqueteA.ToString()], await ConsultarUltimaAsync(e.Bd.Servicio, Consulta(e.OrgA.ToString())));
        Assert.Equal([e.PaqueteB.ToString()], await ConsultarUltimaAsync(e.Bd.Servicio, Consulta(e.OrgB.ToString())));
        Assert.Empty(await ConsultarUltimaAsync(e.Bd.Servicio, Consulta(string.Empty)));
        // Sin variables (una conexión que no pasó por el interceptor), nada.
        Assert.Empty(await Contenedores.ConsultarAsync(e.Bd.Servicio, "SELECT paquete_id::text FROM atlas.contratos_comparticion"));
        // Y no se puede escribir una compartición en otra organización.
        var error = await Assert.ThrowsAsync<PostgresException>(() => Contenedores.EjecutarAsync(e.Bd.Servicio, $"""
            SELECT set_config('atlas.alcance_total', 'off', false), set_config('atlas.organizaciones', '{e.OrgA}', false);
            UPDATE atlas.contratos_comparticion SET puede_exportar = true WHERE true;
            INSERT INTO atlas.contratos_comparticion (id, paquete_id, organizacion_id, puede_exportar, puede_sandbox, vigente_desde, otorgado_por, creado_en, actualizado_en)
            VALUES ('{Guid.NewGuid()}', '{e.PaqueteB}', '{e.OrgB}', true, true, now(), '{Guid.NewGuid()}', now(), now());
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Equal(["false"], await Contenedores.ConsultarAsync(e.Bd.Admin, $"SELECT puede_exportar::text FROM atlas.contratos_comparticion WHERE organizacion_id = '{e.OrgB}'"));
        Assert.Equal(["1"], await Contenedores.ConsultarAsync(e.Bd.Admin, "SELECT count(*)::text FROM atlas.contratos_comparticion WHERE organizacion_id <> '" + e.OrgA + "'"));
    }

    [Fact]
    public async Task El_acceso_del_portal_solo_llega_a_lo_de_la_organizacion_y_RLS_lo_ataja_aunque_el_codigo_se_equivoque()
    {
        var e = await EscenarioAsync();
        var persona = Guid.NewGuid();
        var integrador = new ConcesionRol(RolesAtlas.Integrador, e.OrgA, AlcanceAsignacion.Global, null, "invitacion");

        var vista = await AccesoDe(e.Bd, AlcanceDatos.DeOrganizaciones(e.OrgA), integrador).VistaAsync(Principal(persona, e.OrgA), Ct);
        var paquete = Assert.Single(vista.Paquetes);
        Assert.Equal((e.PaqueteA, NivelPortal.Tecnico, true, false), (paquete.Paquete.Id, paquete.Nivel, paquete.PuedeExportar, paquete.PuedeSandbox));
        Assert.Equal(["GET /tramites/{id}"], Assert.Single(paquete.Contratos).Entrada.Operaciones);

        // Un error que tomara la organización equivocada (B) con el alcance de datos de A: RLS no deja ver la de B.
        var equivocado = await AccesoDe(e.Bd, AlcanceDatos.DeOrganizaciones(e.OrgA), integrador with { OrganizacionId = e.OrgB })
            .VistaAsync(Principal(persona, e.OrgB), Ct);
        Assert.Empty(equivocado.Paquetes);

        // La poda del portal nunca entrega una operación fuera de la selección.
        var acceso = AccesoDe(e.Bd, AlcanceDatos.DeOrganizaciones(e.OrgA), integrador);
        var specs = new SpecsDelPortal(e.Bd.Contratos(AlcanceDatos.DeOrganizaciones(e.OrgA)),
            Microsoft.Extensions.Options.Options.Create(new OpcionesPortal()), new CachePodas(Microsoft.Extensions.Options.Options.Create(new OpcionesPortal())), new SinAplicativos());
        var visto = Assert.Single((await acceso.VistaAsync(Principal(persona, e.OrgA), Ct)).Paquetes);
        var podado = await specs.PodarAsync(visto, Assert.Single(visto.Contratos), null, Ct);
        Assert.NotNull(podado.Spec);
        Assert.Contains("/tramites/{id}", podado.Spec.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("pagos-internos", podado.Spec.Contenido, StringComparison.Ordinal);
        Assert.DoesNotContain("servers", podado.Spec.Contenido, StringComparison.Ordinal);
        Assert.True((await specs.PodarAsync(visto, visto.Contratos[0], null, Ct)).DesdeCache);
    }

    [Fact]
    public async Task Vencida_o_revocada_la_comparticion_o_archivado_el_paquete_se_pierde_el_acceso()
    {
        var e = await EscenarioAsync();
        var persona = Guid.NewGuid();
        var integrador = new ConcesionRol(RolesAtlas.Integrador, e.OrgA, AlcanceAsignacion.Global, null, "invitacion");
        async Task<int> Visibles() =>
            (await AccesoDe(e.Bd, AlcanceDatos.DeOrganizaciones(e.OrgA), integrador).VistaAsync(Principal(persona, e.OrgA), Ct)).Paquetes.Count;

        Assert.Equal(1, await Visibles());
        await Contenedores.EjecutarAsync(e.Bd.Admin, $"UPDATE atlas.contratos_comparticion SET vigente_hasta = now() - interval '1 second', vigente_desde = now() - interval '1 hour' WHERE organizacion_id = '{e.OrgA}'");
        Assert.Equal(0, await Visibles());
        await Contenedores.EjecutarAsync(e.Bd.Admin, $"UPDATE atlas.contratos_comparticion SET vigente_hasta = NULL, revocado_en = now(), revocado_por = gen_random_uuid() WHERE organizacion_id = '{e.OrgA}'");
        Assert.Equal(0, await Visibles());
        await Contenedores.EjecutarAsync(e.Bd.Admin, $"UPDATE atlas.contratos_comparticion SET revocado_en = NULL, revocado_por = NULL, vigente_desde = now() + interval '1 day' WHERE organizacion_id = '{e.OrgA}'");
        Assert.Equal(0, await Visibles());
        await Contenedores.EjecutarAsync(e.Bd.Admin, $"UPDATE atlas.contratos_comparticion SET vigente_desde = now() - interval '1 hour' WHERE organizacion_id = '{e.OrgA}'");
        Assert.Equal(1, await Visibles());
        await Contenedores.EjecutarAsync(e.Bd.Admin, $"UPDATE atlas.contratos_paquete SET estado = 'archivado', archivado_en = now() WHERE id = '{e.PaqueteA}'");
        Assert.Equal(0, await Visibles());
    }

    [Fact]
    public async Task La_revision_fijada_debe_existir_en_el_paquete()
    {
        var e = await EscenarioAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => Contenedores.EjecutarAsync(e.Bd.Admin,
            $"UPDATE atlas.contratos_comparticion SET revision_fijada = 7 WHERE organizacion_id = '{e.OrgA}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public void El_alcance_de_cada_rol_externo_esta_en_la_tabla_y_el_proveedor_solo_ve_lo_concedido()
    {
        // Todo rol con atlas.portal.ver tiene su fila: uno nuevo sin decidir no ve nada, y esta prueba lo recuerda.
        var conPortal = Catalogo.Roles.Where(r => Catalogo.PermisosDe(r.Codigo).Contains(PermisosContratos.PortalVer) && r.Tipo == TipoRol.Externo).Select(r => r.Codigo);
        Assert.Equal(conPortal.Order(StringComparer.Ordinal), AlcanceRolesPortal.Externos.Order(StringComparer.Ordinal));

        var org = AlcanceRolesPortal.Cobertura.Organizacion;
        var paquete = AlcanceRolesPortal.Cobertura.Paquete;
        var aplicativo = AlcanceRolesPortal.Cobertura.Aplicativo;
        Assert.Equal(NivelPortal.Tecnico, AlcanceRolesPortal.Nivel(RolesAtlas.Integrador, TipoRol.Externo, org));
        Assert.Equal(NivelPortal.Ninguno, AlcanceRolesPortal.Nivel(RolesAtlas.Proveedor, TipoRol.Externo, org));
        Assert.Equal(NivelPortal.Tecnico, AlcanceRolesPortal.Nivel(RolesAtlas.Proveedor, TipoRol.Externo, paquete));
        Assert.Equal(NivelPortal.Tecnico, AlcanceRolesPortal.Nivel(RolesAtlas.Proveedor, TipoRol.Externo, aplicativo));
        Assert.Equal(NivelPortal.Resumen, AlcanceRolesPortal.Nivel(RolesAtlas.ClienteFuncional, TipoRol.Externo, org));
        Assert.Equal(NivelPortal.Tecnico, AlcanceRolesPortal.Nivel(RolesAtlas.ClienteFuncional, TipoRol.Externo, paquete));
        Assert.Equal(NivelPortal.Resumen, AlcanceRolesPortal.Nivel(RolesAtlas.AsesorComercial, TipoRol.Externo, paquete));
        Assert.Equal(NivelPortal.Resumen, AlcanceRolesPortal.Nivel(RolesAtlas.AdminOrganizacion, TipoRol.Externo, org));
        Assert.Equal(NivelPortal.Ninguno, AlcanceRolesPortal.Nivel("ext.desconocido", TipoRol.Externo, org));
        Assert.Equal(NivelPortal.Tecnico, AlcanceRolesPortal.Nivel(RolesAtlas.LiderTecnico, TipoRol.Interno, org));
    }

    [Fact]
    public void La_cobertura_de_un_alcance_es_la_organizacion_el_paquete_o_los_contratos_del_aplicativo_y_sus_hijos()
    {
        var (org, otraOrg, paquete, producto, modulo, ajeno) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var aplicativos = new Dictionary<Guid, AplicativoLeido>
        {
            [producto] = new(producto, "producto-demo", "Producto Demo", "producto", null, "satelite", "activo", null, null),
            [modulo] = new(modulo, "modulo-demo", "Módulo Demo", "modulo", producto, "satelite", "activo", null, null),
        };

        Assert.Equal(AlcanceRolesPortal.Cobertura.Organizacion, AccesoPortal.CoberturaDe(AlcanceAsignacion.Global, paquete, modulo, org, aplicativos));
        Assert.Equal(AlcanceRolesPortal.Cobertura.Organizacion, AccesoPortal.CoberturaDe(new(TiposAlcance.Organizacion, org.ToString()), paquete, modulo, org, aplicativos));
        Assert.Null(AccesoPortal.CoberturaDe(new(TiposAlcance.Organizacion, otraOrg.ToString()), paquete, modulo, org, aplicativos));
        Assert.Equal(AlcanceRolesPortal.Cobertura.Paquete, AccesoPortal.CoberturaDe(new(TiposAlcance.Paquete, paquete.ToString()), paquete, modulo, org, aplicativos));
        Assert.Null(AccesoPortal.CoberturaDe(new(TiposAlcance.Paquete, Guid.NewGuid().ToString()), paquete, modulo, org, aplicativos));
        // El producto cubre los contratos de sus módulos; un aplicativo ajeno, no.
        Assert.Equal(AlcanceRolesPortal.Cobertura.Aplicativo, AccesoPortal.CoberturaDe(new(TiposAlcance.Aplicativo, producto.ToString()), paquete, modulo, org, aplicativos));
        Assert.Null(AccesoPortal.CoberturaDe(new(TiposAlcance.Aplicativo, ajeno.ToString()), paquete, modulo, org, aplicativos));
        Assert.Null(AccesoPortal.CoberturaDe(new(TiposAlcance.Documento, paquete.ToString()), paquete, modulo, org, aplicativos));
    }

    [Fact]
    public void Los_cambios_solo_muestran_los_de_operaciones_compartidas()
    {
        const string Changelog = """
            [
              {"id":"response-property-removed","text":"se quitó id","level":3,"operation":"GET","path":"/tramites/{id}"},
              {"id":"new-required-request-property","text":"pago interno","level":3,"operation":"POST","path":"/pagos-internos"},
              {"id":"api-schema-removed","text":"se quitó un esquema","level":3},
              {"id":"endpoint-added","text":"nuevo","level":1,"operation":"get","path":"/tramites/{id}"}
            ]
            """;

        var cambios = EndpointsPortal.FiltrarCambios(Changelog, ["GET /tramites/{id}"]);

        Assert.Equal(["response-property-removed", "endpoint-added"], cambios.Select(c => c.Id));
        Assert.Empty(EndpointsPortal.FiltrarCambios("no es json", ["GET /tramites/{id}"]));
        Assert.Empty(EndpointsPortal.FiltrarCambios("{}", ["GET /tramites/{id}"]));
    }

    [Fact]
    public void La_vigencia_termina_despues_de_empezar_en_el_futuro_y_es_obligatoria_con_un_proveedor()
    {
        var ahora = DateTimeOffset.UtcNow;

        Assert.Empty(ReglasComparticiones.Vigencia(null, null, TipoOrganizacion.Cliente, ahora).Errores);
        Assert.Equal(ahora, ReglasComparticiones.Vigencia(null, null, TipoOrganizacion.Cliente, ahora).Desde);
        Assert.Contains("vigenteHasta", ReglasComparticiones.Vigencia(null, null, TipoOrganizacion.Proveedor, ahora).Errores.Keys);
        Assert.Contains("vigenteHasta", ReglasComparticiones.Vigencia(ahora.AddDays(2), ahora.AddDays(1), TipoOrganizacion.Cliente, ahora).Errores.Keys);
        Assert.Contains("vigenteHasta", ReglasComparticiones.Vigencia(ahora.AddDays(-3), ahora.AddDays(-1), TipoOrganizacion.Cliente, ahora).Errores.Keys);

        var c = new ComparticionPaquete { PaqueteId = Guid.NewGuid(), OrganizacionId = Guid.NewGuid(), VigenteDesde = ahora.AddDays(-1), OtorgadoPor = Guid.NewGuid() };
        Assert.Equal(ReglasComparticiones.Vigente, ReglasComparticiones.Estado(c, ahora));
        Assert.Equal(ReglasComparticiones.Programada, ReglasComparticiones.Estado(c, ahora.AddDays(-2)));
        c.VigenteHasta = ahora;
        Assert.Equal(ReglasComparticiones.Vencida, ReglasComparticiones.Estado(c, ahora));
        c.RevocadoEn = ahora;
        Assert.Equal(ReglasComparticiones.Revocada, ReglasComparticiones.Estado(c, ahora));
    }

    private static ClaimsPrincipal Principal(Guid persona, Guid organizacion) => new(new ClaimsIdentity(
        [new Claim(EndpointsPlataforma.ClaimPersona, persona.ToString()), new Claim(EndpointsPlataforma.ClaimOrganizacion, organizacion.ToString())], "prueba"));

    private static AccesoPortal AccesoDe(BaseDePrueba bd, AlcanceDatos alcance, params ConcesionRol[] concesiones) =>
        new(bd.Contratos(alcance), bd.Plataforma(), new PermisosFijos(PermisosEfectivos.Desde(Catalogo, concesiones)), Catalogo, new SinAplicativos(), TimeProvider.System);

    private sealed class PermisosFijos(PermisosEfectivos permisos) : IPermisosDeSesion
    {
        public Task<PermisosEfectivos> ObtenerAsync(ClaimsPrincipal usuario, CancellationToken ct = default) => Task.FromResult(permisos);
    }

    private sealed class SinAplicativos : ILectorAplicativos
    {
        public Task<IReadOnlyList<AplicativoLeido>> ListarAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AplicativoLeido>>([]);
    }

    /// <summary>Corre varias sentencias en una conexión y devuelve la primera columna de la última consulta.</summary>
    private static async Task<List<string>> ConsultarUltimaAsync(string cadena, string sql)
    {
        await using var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync(Ct);
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync(Ct);
        var filas = new List<string>();
        do
        {
            filas.Clear();
            while (await lector.ReadAsync(Ct))
            {
                filas.Add(lector.GetValue(0)?.ToString() ?? string.Empty);
            }
        }
        while (await lector.NextResultAsync(Ct));
        return filas;
    }
}
