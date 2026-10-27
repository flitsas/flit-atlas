using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Aprovisionamiento;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Personas;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using static Flit.Atlas.Identidad.Personas.EndpointsDirectorio;

namespace Flit.Atlas.Api.Tests;

/// <summary>
/// HU 2.2 (ATLAS-ADR-0030): sincronización diaria de personas con un Graph simulado (<see cref="GraphSimulado"/>), nada
/// contra Microsoft. Cada prueba usa una base nueva: la sincronización recorre a todos los empleados enlazados.
/// </summary>
public class SincronizacionPersonasTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string IpNueva() => $"2001:db8::30:{Random.Shared.Next(1, 0xffff):x}";

    /// <summary>Bytes de una «foto» ficticia (no hace falta una imagen real: Atlas no la decodifica).</summary>
    private static byte[] FotoFicticia(byte semilla, int largo = 2048) => [.. Enumerable.Range(0, largo).Select(i => (byte)(i * semilla))];

    /// <summary>atlas-api sobre una base nueva con el Graph simulado y el trabajo diario apagado (solo corridas pedidas).</summary>
    private WebApplicationFactory<Program> Api(string baseDeDatos, GraphSimulado graph, Dictionary<string, string>? configuracion = null)
    {
        var config = new Dictionary<string, string>
        {
            [$"{OpcionesSincronizacionPersonas.Seccion}:Habilitada"] = "false",
            [$"{OpcionesSincronizacionPersonas.Seccion}:EsperaReintento"] = "00:00:00.050",
            [$"{OpcionesLimiteTasa.Seccion}:PorUsuario"] = "1000",
            [$"{OpcionesLimiteTasa.Seccion}:PorIp"] = "1000",
        };
        foreach (var (clave, valor) in configuracion ?? [])
        {
            config[clave] = valor;
        }
        return entorno.Api(baseDeDatos: baseDeDatos, configuracion: config).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Singleton<IDirectorioEntra>(graph.Directorio()))));
    }

    private static async Task<EstadoSincronizacion?> SincronizarAsync(WebApplicationFactory<Program> api, Guid? actor = null)
    {
        await using var alcance = api.Services.CreateAsyncScope();
        return await alcance.ServiceProvider.GetRequiredService<SincronizadorPersonas>()
            .SincronizarAsync(actor is null ? DisparoSincronizacion.Programada : DisparoSincronizacion.Manual, actor, ct: Ct);
    }

    private async Task<Guid> OrganizacionInternaAsync(string baseDeDatos)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var interna = await plataforma.Organizaciones.FirstOrDefaultAsync(o => o.Tipo == TipoOrganizacion.Interna, Ct);
        if (interna is null)
        {
            interna = new Organizacion { Nombre = AprovisionadorEmpleados.NombreOrganizacionInterna, Tipo = TipoOrganizacion.Interna };
            plataforma.Organizaciones.Add(interna);
            await plataforma.SaveChangesAsync(Ct);
        }
        return interna.Id;
    }

    /// <summary>Empleado en Atlas (enlazado a Entra si trae <paramref name="oid"/>), con su usuario de Identity.</summary>
    private async Task<(Guid PersonaId, Guid UsuarioId)> EmpleadoAsync(
        WebApplicationFactory<Program> api, string baseDeDatos, string nombre, string correo, Guid? oid,
        EstadoPersona estado = EstadoPersona.Activa, string? rol = null, OrigenAsignacion origen = OrigenAsignacion.Manual)
    {
        var interna = await OrganizacionInternaAsync(baseDeDatos);
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        var persona = new Persona
        {
            Tipo = TipoPersona.Empleado, OrganizacionId = interna, Nombre = nombre, Correo = correo, EntraOid = oid, Estado = estado,
        };
        plataforma.Personas.Add(persona);
        if (rol is not null)
        {
            var rolId = await plataforma.Roles.Where(r => r.Codigo == rol).Select(r => r.Id).SingleAsync(Ct);
            plataforma.AsignacionesRol.Add(new AsignacionRol
            {
                PersonaId = persona.Id, RolId = rolId, OrganizacionId = interna, VigenteDesde = DateTimeOffset.UtcNow.AddMinutes(-1),
                Origen = origen,
            });
        }
        await plataforma.SaveChangesAsync(Ct);

        await using var alcance = api.Services.CreateAsyncScope();
        var usuarios = alcance.ServiceProvider.GetRequiredService<UserManager<UsuarioAtlas>>();
        var usuario = new UsuarioAtlas { PersonaId = persona.Id, UserName = correo, Email = correo, EmailConfirmed = true, CreadoEn = DateTimeOffset.UtcNow };
        Assert.True((await usuarios.CreateAsync(usuario)).Succeeded);
        return (persona.Id, usuario.Id);
    }

    private async Task<Persona> PersonaAsync(string baseDeDatos, Guid id)
    {
        await using var plataforma = entorno.Plataforma(baseDeDatos);
        return await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Id == id, Ct);
    }

    private static async Task<Navegador> EntrarAsync(WebApplicationFactory<Program> api, Guid usuarioId)
    {
        var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarComoAsync(usuarioId)).StatusCode);
        return navegador;
    }

    [Fact]
    public async Task Refresca_los_datos_y_la_foto_y_deja_inactivas_las_cuentas_deshabilitadas_o_borradas_cerrando_sus_sesiones()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph);

        var ana = graph.Agregar("Ana Demo Pérez", "ana.demo@ejemplo.com", foto: FotoFicticia(3));
        ana.Cargo = "Desarrolladora";
        ana.Area = "Tecnología";
        ana.Movil = "+57 300 000 0001";
        var beto = graph.Agregar("Beto Demo", "beto.demo@ejemplo.com", habilitada: false);
        var (anaId, _) = await EmpleadoAsync(api, cadena, "Ana Vieja", ana.Correo!, ana.Id);
        var (betoId, betoUsuario) = await EmpleadoAsync(api, cadena, "Beto Demo", beto.Correo!, beto.Id);
        // Carla está enlazada pero ya no existe en Entra (404): cuenta como deshabilitada.
        var (carlaId, _) = await EmpleadoAsync(api, cadena, "Carla Demo", "carla.demo@ejemplo.com", Guid.NewGuid());
        var otro = await EmpleadoAsync(api, cadena, "Otro Demo", "otro.demo@ejemplo.com", graph.Agregar("Otro Demo", "otro.demo@ejemplo.com").Id);
        var cuarta = await EmpleadoAsync(api, cadena, "Cuarta Demo", "cuarta.demo@ejemplo.com", graph.Agregar("Cuarta Demo", "cuarta.demo@ejemplo.com").Id);
        using var sesionBeto = await EntrarAsync(api, betoUsuario);
        Assert.Equal(HttpStatusCode.OK, (await sesionBeto.GetAsync("/api/v1/plataforma/yo")).StatusCode);

        var estado = await SincronizarAsync(api);

        Assert.NotNull(estado);
        Assert.Equal("exitosa", estado.Resultado);
        Assert.Equal("enlazadas", estado.Origen);
        Assert.Equal((0, 1, 2, 0, 2), (estado.Creadas, estado.Actualizadas, estado.Deshabilitadas, estado.Reactivadas, estado.SinCambios));

        var anaEnAtlas = await PersonaAsync(cadena, anaId);
        Assert.Equal(("Ana Demo Pérez", "Desarrolladora", "Tecnología", "+57 300 000 0001"), (anaEnAtlas.Nombre, anaEnAtlas.Cargo, anaEnAtlas.Area, anaEnAtlas.Telefono));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(ana.Foto!)), anaEnAtlas.FotoHash);
        Assert.NotNull(anaEnAtlas.SincronizadaEn);
        Assert.Equal((EstadoPersona.Inactiva, true), ((await PersonaAsync(cadena, betoId)).Estado, (await PersonaAsync(cadena, betoId)).InactivaPorEntra));
        Assert.Equal(EstadoPersona.Inactiva, (await PersonaAsync(cadena, carlaId)).Estado);
        Assert.Equal(EstadoPersona.Activa, (await PersonaAsync(cadena, otro.PersonaId)).Estado);
        _ = cuarta;

        // La sesión abierta de Beto se cortó (sello de seguridad) y quedó el evento con el motivo.
        Assert.Equal(HttpStatusCode.Unauthorized, (await sesionBeto.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.True(await Auditoria.RegistradoAsync(entorno, TiposEventoSeguridad.SesionesInvalidadas, betoId.ToString(), cadena));

        // Un evento de auditoría por corrida, con conteos y sin datos personales.
        await using var plataforma = entorno.Plataforma(cadena);
        var evento = await plataforma.Auditoria.SingleAsync(a => a.Accion == SincronizadorPersonas.AccionAuditoria, Ct);
        Assert.Equal(estado.Id.ToString(), evento.RecursoId);
        Assert.Equal(TipoActor.Sistema, evento.ActorTipo);
        using (var datos = System.Text.Json.JsonDocument.Parse(evento.Datos!))
        {
            Assert.Equal(2, datos.RootElement.GetProperty("deshabilitadas").GetInt32());
            Assert.Equal("programada", datos.RootElement.GetProperty("disparo").GetString());
        }
        foreach (var personal in new[] { "Ana", "ejemplo.com", "+57", "Tecnolog" })
        {
            Assert.DoesNotContain(personal, evento.Datos, StringComparison.Ordinal);
        }

        // Nada cambió: la segunda corrida no escribe fotos ni cuenta actualizaciones.
        var segunda = await SincronizarAsync(api);
        Assert.Equal((0, 0, 0, 5), (segunda!.Creadas, segunda.Actualizadas, segunda.Deshabilitadas, segunda.SinCambios));
    }

    /// <summary>
    /// F4-SEC-08 (ATLAS-ADR-0049): la corrida diaria recalcula los roles que vienen de grupos de Entra con
    /// <c>transitiveMemberOf</c> y, si cambian, invalida las sesiones (sello de seguridad); quien sigue igual no pierde la
    /// sesión. F4-SEC-07: una persona sin oid con roles no se enlaza por correo; queda el evento.
    /// </summary>
    [Fact]
    public async Task Recalcula_los_roles_de_los_grupos_de_Entra_cierra_las_sesiones_si_cambian_y_no_enlaza_por_correo_a_quien_tiene_roles()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph, new() { [$"{OpcionesSincronizacionPersonas.Seccion}:Origen"] = "Tenant" });
        var grupoAdmin = await entorno.GrupoConRolAsync(RolesAtlas.Superadmin, cadena);
        var grupoDev = await entorno.GrupoConRolAsync(RolesAtlas.Desarrollador, cadena);
        var ana = graph.Agregar("Ana Grupos", "ana.grupos@ejemplo.com");
        ana.Grupos = [grupoAdmin];
        var beto = graph.Agregar("Beto Grupos", "beto.grupos@ejemplo.com");
        beto.Grupos = [grupoAdmin];
        var (anaId, anaUsuario) = await EmpleadoAsync(api, cadena, ana.Nombre, ana.Correo!, ana.Id, rol: RolesAtlas.Superadmin, origen: OrigenAsignacion.GrupoEntra);
        var (betoId, betoUsuario) = await EmpleadoAsync(api, cadena, beto.Nombre, beto.Correo!, beto.Id, rol: RolesAtlas.Superadmin, origen: OrigenAsignacion.GrupoEntra);
        // Carla existía sin oid y con un rol asignado a mano; en Entra aparece otra cuenta con su correo.
        var carla = graph.Agregar("Carla Grupos", "carla.grupos@ejemplo.com");
        var (carlaId, _) = await EmpleadoAsync(api, cadena, "Carla Grupos", carla.Correo!, null, rol: RolesAtlas.Desarrollador);
        using var sesionAna = await EntrarAsync(api, anaUsuario);
        using var sesionBeto = await EntrarAsync(api, betoUsuario);

        // Ana salió del grupo de administradores y entró al de desarrolladores, sin volver a entrar a Atlas.
        ana.Grupos = [grupoDev];
        var estado = await SincronizarAsync(api);

        Assert.Equal("exitosa", estado!.Resultado);
        Assert.Contains(graph.Peticiones, p => p.Contains($"/users/{ana.Id}/transitiveMemberOf/", StringComparison.Ordinal));
        await using (var plataforma = entorno.Plataforma(cadena))
        {
            var rolesAna = await plataforma.AsignacionesRol.Where(a => a.PersonaId == anaId)
                .Join(plataforma.Roles, a => a.RolId, r => r.Id, (a, r) => new { r.Codigo, a.Origen }).ToListAsync(Ct);
            var unico = Assert.Single(rolesAna);
            Assert.Equal((RolesAtlas.Desarrollador, OrigenAsignacion.GrupoEntra), (unico.Codigo, unico.Origen));
            Assert.Equal(1, await plataforma.AsignacionesRol.CountAsync(a => a.PersonaId == betoId, Ct));
            // Carla no quedó enlazada a la cuenta nueva ni nadie heredó su rol.
            Assert.Null((await plataforma.Personas.AsNoTracking().SingleAsync(p => p.Id == carlaId, Ct)).EntraOid);
            Assert.False(await plataforma.Personas.AnyAsync(p => p.EntraOid == carla.Id, Ct));
        }
        // La sesión de Ana se cortó (pierde atlas.superadmin ya); la de Beto sigue.
        Assert.Equal(HttpStatusCode.Unauthorized, (await sesionAna.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sesionBeto.GetAsync("/api/v1/plataforma/yo")).StatusCode);
        Assert.True(await Auditoria.RegistradoAsync(entorno, TiposEventoSeguridad.SesionesInvalidadas, anaId.ToString(), cadena));
        Assert.True(await Auditoria.RegistradoAsync(entorno, TiposEventoSeguridad.VinculoPorCorreoRechazado, carlaId.ToString(), cadena));

        // Sin cambios en Entra, la siguiente corrida no toca roles ni sesiones.
        using var sesionAnaNueva = await EntrarAsync(api, anaUsuario);
        await SincronizarAsync(api);
        Assert.Equal(HttpStatusCode.OK, (await sesionAnaNueva.GetAsync("/api/v1/plataforma/yo")).StatusCode);
    }

    [Fact]
    public async Task Al_rehabilitarse_en_Entra_la_persona_vuelve_a_activa_pero_una_inactiva_por_otro_motivo_o_suspendida_no()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph);
        var beto = graph.Agregar("Beto Demo", "beto.demo@ejemplo.com", habilitada: false);
        var (betoId, _) = await EmpleadoAsync(api, cadena, "Beto Demo", beto.Correo!, beto.Id);
        var dora = graph.Agregar("Dora Demo", "dora.demo@ejemplo.com");
        var (doraId, _) = await EmpleadoAsync(api, cadena, "Dora Demo", dora.Correo!, dora.Id, EstadoPersona.Inactiva);
        var eva = graph.Agregar("Eva Demo", "eva.demo@ejemplo.com", habilitada: false);
        var (evaId, _) = await EmpleadoAsync(api, cadena, "Eva Demo", eva.Correo!, eva.Id, EstadoPersona.Suspendida);
        foreach (var i in Enumerable.Range(0, 3))
        {
            await EmpleadoAsync(api, cadena, $"Relleno {i}", $"relleno.{i}@ejemplo.com", graph.Agregar($"Relleno {i}", $"relleno.{i}@ejemplo.com").Id);
        }

        Assert.Equal(1, (await SincronizarAsync(api))!.Deshabilitadas);
        beto.Habilitada = true;
        eva.Habilitada = true;
        var estado = await SincronizarAsync(api);

        Assert.Equal((1, 0), (estado!.Reactivadas, estado.Deshabilitadas));
        var betoEnAtlas = await PersonaAsync(cadena, betoId);
        Assert.Equal((EstadoPersona.Activa, false), (betoEnAtlas.Estado, betoEnAtlas.InactivaPorEntra));
        // Dora estaba inactiva desde antes (no por Entra) y Eva la suspendió un administrador: la sincronización no decide por ellos.
        Assert.Equal(EstadoPersona.Inactiva, (await PersonaAsync(cadena, doraId)).Estado);
        Assert.Equal(EstadoPersona.Suspendida, (await PersonaAsync(cadena, evaId)).Estado);
    }

    [Fact]
    public async Task Con_el_origen_grupo_crea_y_enlaza_empleados_sin_invitados_ni_deshabilitados_nuevos_ni_correos_ajenos()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado { TamanoPagina = 2 };
        var grupo = Guid.NewGuid();
        await using var api = Api(cadena, graph, new()
        {
            [$"{OpcionesSincronizacionPersonas.Seccion}:Origen"] = "Grupo",
            [$"{OpcionesSincronizacionPersonas.Seccion}:GrupoEmpleados"] = grupo.ToString(),
        });
        var nueva = graph.Agregar("Nueva Demo", "Nueva.Demo@Ejemplo.com", foto: FotoFicticia(5));
        var porCorreo = graph.Agregar("Enlazada Por Correo", "enlazada.demo@ejemplo.com");
        graph.Agregar("Invitada Demo", "invitada.demo@ejemplo.com").Tipo = "Guest";
        graph.Agregar("Deshabilitada Demo", "deshabilitada.demo@ejemplo.com", habilitada: false);
        graph.Agregar("Sin Correo Demo");
        graph.Usuarios.Values.Single(u => u.Nombre == "Sin Correo Demo").Correo = null;
        graph.Agregar("Correo De Externa", "externa.demo@ejemplo.com");
        graph.Agregar("Fuera Del Grupo", "fuera.demo@ejemplo.com").EnGrupo = false;
        // Ya existían: un empleado sin oid (p. ej. cargado del reparto de responsables) y una externa con un correo que Entra también tiene.
        var (enlazadaId, _) = await EmpleadoAsync(api, cadena, "Enlazada", porCorreo.Correo!, null);
        await using (var plataforma = entorno.Plataforma(cadena))
        {
            var cliente = new Organizacion { Nombre = "Organismo Demo", Tipo = TipoOrganizacion.Cliente };
            plataforma.AddRange(cliente, new Persona { Tipo = TipoPersona.Externo, OrganizacionId = cliente.Id, Nombre = "Externa Demo", Correo = "externa.demo@ejemplo.com" });
            await plataforma.SaveChangesAsync(Ct);
        }

        var estado = await SincronizarAsync(api);

        // Los 6 del grupo llegan en 3 páginas (nextLink).
        Assert.Equal(3, graph.Peticiones.Count(p => p.Contains("/transitiveMembers/", StringComparison.Ordinal)));
        Assert.Equal((1, 1, 0), (estado!.Creadas, estado.Actualizadas, estado.Deshabilitadas));
        Assert.Equal(4, estado.Omitidas);
        await using var lectura = entorno.Plataforma(cadena);
        var creada = await lectura.Personas.SingleAsync(p => p.EntraOid == nueva.Id, Ct);
        Assert.Equal(("nueva.demo@ejemplo.com", TipoPersona.Empleado, EstadoPersona.Activa), (creada.Correo, creada.Tipo, creada.Estado));
        Assert.NotNull(creada.FotoHash);
        Assert.Equal(porCorreo.Id, (await PersonaAsync(cadena, enlazadaId)).EntraOid);
        Assert.Equal("Enlazada Por Correo", (await PersonaAsync(cadena, enlazadaId)).Nombre);
        foreach (var correo in new[] { "invitada.demo@ejemplo.com", "deshabilitada.demo@ejemplo.com", "fuera.demo@ejemplo.com" })
        {
            Assert.False(await lectura.Personas.AnyAsync(p => p.Correo == correo, Ct), correo);
        }
        Assert.Equal(TipoPersona.Externo, (await lectura.Personas.SingleAsync(p => p.Correo == "externa.demo@ejemplo.com", Ct)).Tipo);
    }

    [Fact]
    public async Task Nunca_toca_la_cuenta_de_emergencia_ni_a_los_externos()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph, new() { [$"{OpcionesSincronizacionPersonas.Seccion}:Origen"] = "Tenant" });
        // Aunque Entra tuviera una cuenta deshabilitada con el mismo correo (y alguien le hubiera puesto un oid).
        var falsa = graph.Agregar("Emergencia Falsa", "admin.emergencia@ejemplo.com", habilitada: false);
        var (emergenciaId, _) = await EmpleadoAsync(api, cadena, "Emergencia Demo", "admin.emergencia@ejemplo.com", falsa.Id, rol: RolesAtlas.Superadmin, origen: OrigenAsignacion.Emergencia);
        graph.Agregar("Nombre Nuevo De Externa", "externa.demo@ejemplo.com");
        Guid externaId;
        await using (var plataforma = entorno.Plataforma(cadena))
        {
            var cliente = new Organizacion { Nombre = "Organismo Demo", Tipo = TipoOrganizacion.Cliente };
            var externa = new Persona { Tipo = TipoPersona.Externo, OrganizacionId = cliente.Id, Nombre = "Externa Demo", Correo = "externa.demo@ejemplo.com" };
            plataforma.AddRange(cliente, externa);
            await plataforma.SaveChangesAsync(Ct);
            externaId = externa.Id;
        }

        await SincronizarAsync(api);

        var emergencia = await PersonaAsync(cadena, emergenciaId);
        Assert.Equal((EstadoPersona.Activa, "Emergencia Demo", (DateTimeOffset?)null), (emergencia.Estado, emergencia.Nombre, emergencia.SincronizadaEn));
        var externaEnAtlas = await PersonaAsync(cadena, externaId);
        Assert.Equal(("Externa Demo", TipoPersona.Externo, (Guid?)null), (externaEnAtlas.Nombre, externaEnAtlas.Tipo, externaEnAtlas.EntraOid));
    }

    [Fact]
    public async Task El_freno_retiene_las_deshabilitaciones_masivas()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph);
        var ids = new List<Guid>();
        foreach (var i in Enumerable.Range(0, 5))
        {
            // Ninguna existe en Entra (p. ej. un registro de la app que apunta a otro tenant): todas responderían 404.
            ids.Add((await EmpleadoAsync(api, cadena, $"Persona Demo {i}", $"persona.{i}@ejemplo.com", Guid.NewGuid())).PersonaId);
        }

        var estado = await SincronizarAsync(api);

        Assert.Equal((0, 5), (estado!.Deshabilitadas, estado.Retenidas));
        foreach (var id in ids)
        {
            Assert.Equal(EstadoPersona.Activa, (await PersonaAsync(cadena, id)).Estado);
        }
    }

    [Fact]
    public async Task Una_sola_corrida_a_la_vez_y_reintenta_si_Graph_no_responde()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph, new() { [$"{OpcionesSincronizacionPersonas.Seccion}:Reintentos"] = "3" });
        var ana = graph.Agregar("Ana Demo", "ana.demo@ejemplo.com");
        await EmpleadoAsync(api, cadena, "Ana Demo", ana.Correo!, ana.Id);

        // Otra réplica tiene el candado.
        await using (var otra = new NpgsqlConnection(cadena))
        {
            await otra.OpenAsync(Ct);
            await using (var tomar = new NpgsqlCommand($"SELECT pg_advisory_lock({SincronizadorPersonas.Candado})", otra))
            {
                await tomar.ExecuteNonQueryAsync(Ct);
            }
            await Assert.ThrowsAsync<SincronizacionEnCursoException>(() => SincronizarAsync(api));
            var trabajo = api.Services.GetRequiredService<TrabajoSincronizacionPersonas>();
            Assert.Null(await trabajo.CorrerConReintentosAsync(DisparoSincronizacion.Programada, null, Ct));
            // El candado es de la sesión: cerrar la conexión la devuelve al pool sin soltarlo.
            await using var soltar = new NpgsqlCommand($"SELECT pg_advisory_unlock({SincronizadorPersonas.Candado})", otra);
            await soltar.ExecuteNonQueryAsync(Ct);
        }

        // Graph responde 503 dos veces: el trabajo espera, reintenta y la tercera vez termina.
        graph.FallasPendientes = 2;
        var estado = await api.Services.GetRequiredService<TrabajoSincronizacionPersonas>()
            .CorrerConReintentosAsync(DisparoSincronizacion.Programada, null, Ct);
        Assert.Equal("exitosa", estado!.Resultado);
        Assert.Equal(0, graph.FallasPendientes);

        // Sin intentos suficientes queda la corrida fallida registrada (solo el tipo de error) y no lanza.
        graph.FallasPendientes = 100;
        Assert.Null(await api.Services.GetRequiredService<TrabajoSincronizacionPersonas>()
            .CorrerConReintentosAsync(DisparoSincronizacion.Programada, null, Ct));
        await using var plataforma = entorno.Plataforma(cadena);
        var ultima = await SincronizadorPersonas.LeerEstadoAsync(plataforma, Ct);
        Assert.Equal(("fallida", nameof(HttpRequestException)), (ultima!.Resultado, ultima.Error));

        // La franja ya hecha por otra réplica no se repite.
        graph.FallasPendientes = 0;
        await using (var alcance = api.Services.CreateAsyncScope())
        {
            var sincronizador = alcance.ServiceProvider.GetRequiredService<SincronizadorPersonas>();
            var hecha = await sincronizador.SincronizarAsync(DisparoSincronizacion.Programada, ct: Ct);
            Assert.Null(await sincronizador.SincronizarAsync(DisparoSincronizacion.Programada, noAntesDe: hecha!.Inicio.AddMinutes(-1), ct: Ct));
        }
    }

    [Fact]
    public async Task Una_falla_que_reintentar_no_arregla_queda_registrada_y_no_tumba_el_trabajo()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph, new() { [$"{OpcionesSincronizacionPersonas.Seccion}:Reintentos"] = "3" });
        var ana = graph.Agregar("Ana Demo", "ana.demo@ejemplo.com");
        await EmpleadoAsync(api, cadena, "Ana Demo", ana.Correo!, ana.Id);

        // Como QA el 2026-10-23: el token de aplicación no se puede pedir (IDW10503). Antes se escapaba del
        // BackgroundService y el host (StopHost) tumbaba atlas-api.
        graph.ErrorDelToken = new InvalidOperationException("IDW10503: Cannot determine the cloud Instance.");
        var trabajo = api.Services.GetRequiredService<TrabajoSincronizacionPersonas>();
        Assert.Null(await trabajo.CorrerConReintentosAsync(DisparoSincronizacion.Manual, null, Ct));
        Assert.Empty(graph.Peticiones);
        await using (var plataforma = entorno.Plataforma(cadena))
        {
            var ultima = await SincronizadorPersonas.LeerEstadoAsync(plataforma, Ct);
            Assert.Equal(("fallida", nameof(InvalidOperationException)), (ultima!.Resultado, ultima.Error));
        }

        // Corregida la causa, la siguiente corrida termina bien.
        graph.ErrorDelToken = null;
        Assert.Equal("exitosa", (await trabajo.CorrerConReintentosAsync(DisparoSincronizacion.Manual, null, Ct))!.Resultado);
    }

    [Fact]
    public async Task La_proxima_corrida_es_a_la_hora_de_Bogota()
    {
        var cinco = new TimeSpan(5, 0, 0);
        // 04:59 en Bogotá (UTC−5) → hoy a las 05:00; a las 05:00 en punto → mañana.
        Assert.Equal(DateTimeOffset.Parse("2026-10-23T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TrabajoSincronizacionPersonas.ProximaCorrida(DateTimeOffset.Parse("2026-10-23T09:59:00Z", System.Globalization.CultureInfo.InvariantCulture), cinco));
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TrabajoSincronizacionPersonas.ProximaCorrida(DateTimeOffset.Parse("2026-10-23T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture), cinco));
        // 23:30 en Bogotá es otro día en UTC.
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T04:30:00Z", System.Globalization.CultureInfo.InvariantCulture),
            TrabajoSincronizacionPersonas.ProximaCorrida(DateTimeOffset.Parse("2026-10-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), new TimeSpan(23, 30, 0)));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task El_directorio_muestra_empleados_con_foto_y_el_telefono_solo_con_ver_contacto()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph);
        var ana = graph.Agregar("Ana Directorio", "ana.directorio@ejemplo.com", foto: FotoFicticia(7));
        ana.Area = "Tecnología";
        ana.TelefonosTrabajo = ["+57 601 000 0000"];
        var (anaId, _) = await EmpleadoAsync(api, cadena, "Ana", ana.Correo!, ana.Id);
        var lectora = await EmpleadoAsync(api, cadena, "Lectora Demo", "lectora.demo@ejemplo.com", null);
        var desarrollador = await EmpleadoAsync(api, cadena, "Desarrollador Demo", "dev.demo@ejemplo.com", null, rol: RolesAtlas.Desarrollador);
        await EmpleadoAsync(api, cadena, "Emergencia Demo", "admin.emergencia@ejemplo.com", null, rol: RolesAtlas.Superadmin, origen: OrigenAsignacion.Emergencia);
        await SincronizarAsync(api);

        using var comoLectora = await EntrarAsync(api, lectora.UsuarioId);
        var sinContacto = await (await comoLectora.GetAsync("/api/v1/plataforma/directorio?q=tecnologia")).Content.ReadFromJsonAsync<ResultadoDirectorio>(Ct);
        var vista = Assert.Single(sinContacto!.Personas);
        Assert.Equal(("Ana Directorio", "Tecnología", (string?)null, false), (vista.Nombre, vista.Area, vista.Telefono, sinContacto.VeContacto));
        Assert.StartsWith($"/api/v1/plataforma/personas/{anaId}/foto?v=", vista.Foto, StringComparison.Ordinal);
        var todos = await (await comoLectora.GetAsync("/api/v1/plataforma/directorio")).Content.ReadFromJsonAsync<ResultadoDirectorio>(Ct);
        Assert.DoesNotContain(todos!.Personas, p => p.Nombre == "Emergencia Demo");
        Assert.Equal(HttpStatusCode.BadRequest, (await comoLectora.GetAsync("/api/v1/plataforma/directorio?q=a")).StatusCode);

        // La foto con su ETag y caché privada; con el mismo ETag, 304.
        var foto = await comoLectora.GetAsync(vista.Foto!);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
        Assert.Equal("image/jpeg", foto.Content.Headers.ContentType!.MediaType);
        Assert.Equal(ana.Foto, await foto.Content.ReadAsByteArrayAsync(Ct));
        Assert.Contains("private", foto.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        using (var condicional = new HttpRequestMessage(HttpMethod.Get, vista.Foto))
        {
            condicional.Headers.Add("Cookie", comoLectora.Cookie);
            condicional.Headers.Add("X-Forwarded-For", comoLectora.Ip);
            condicional.Headers.IfNoneMatch.Add(foto.Headers.ETag!);
            using var cliente = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            Assert.Equal(HttpStatusCode.NotModified, (await cliente.SendAsync(condicional, Ct)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await comoLectora.GetAsync($"/api/v1/plataforma/personas/{lectora.PersonaId}/foto")).StatusCode);

        // /yo trae la foto propia para el avatar del shell.
        using var comoAna = await EntrarAsync(api, (await UsuarioDeAsync(cadena, anaId)));
        var yo = await (await comoAna.GetAsync("/api/v1/plataforma/yo")).Content.ReadFromJsonAsync<EndpointsIdentidad.Yo>(Ct);
        Assert.Equal(vista.Foto, yo!.Foto);

        using var comoDesarrollador = await EntrarAsync(api, desarrollador.UsuarioId);
        var conContacto = await (await comoDesarrollador.GetAsync($"/api/v1/plataforma/directorio/{anaId}")).Content.ReadFromJsonAsync<PersonaDirectorio>(Ct);
        Assert.Equal("+57 601 000 0000", conContacto!.Telefono);
        Assert.Equal(HttpStatusCode.NotFound, (await comoDesarrollador.GetAsync($"/api/v1/plataforma/directorio/{Guid.NewGuid()}")).StatusCode);
    }

    private async Task<Guid> UsuarioDeAsync(string cadena, Guid personaId)
    {
        await using var identidad = entorno.Identidad(cadena);
        return await identidad.Users.Where(u => u.PersonaId == personaId).Select(u => u.Id).SingleAsync(Ct);
    }

    [Fact]
    public async Task Sincronizar_ahora_corre_en_segundo_plano_y_queda_en_el_estado_con_el_actor()
    {
        var cadena = await entorno.BaseNuevaAsync();
        var graph = new GraphSimulado();
        await using var api = Api(cadena, graph);
        var ana = graph.Agregar("Ana Demo", "ana.demo@ejemplo.com");
        await EmpleadoAsync(api, cadena, "Ana", ana.Correo!, ana.Id);
        var admin = await EmpleadoAsync(api, cadena, "Admin Demo", "admin.demo@ejemplo.com", null, rol: RolesAtlas.Superadmin);
        using var navegador = await EntrarAsync(api, admin.UsuarioId);

        var antes = await (await navegador.GetAsync("/api/v1/plataforma/directorio/sincronizacion")).Content.ReadFromJsonAsync<VistaSincronizacion>(Ct);
        Assert.Equal((true, false, "enlazadas", "05:00", (EstadoSincronizacion?)null), (antes!.Disponible, antes.Programada, antes.Origen, antes.Hora, antes.Ultima));

        Assert.Equal(HttpStatusCode.Accepted, (await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/directorio/sincronizacion")).StatusCode);
        VistaSincronizacion? despues = null;
        for (var i = 0; i < 100 && despues?.Ultima is null; i++)
        {
            await Task.Delay(100, Ct);
            despues = await (await navegador.GetAsync("/api/v1/plataforma/directorio/sincronizacion")).Content.ReadFromJsonAsync<VistaSincronizacion>(Ct);
        }
        Assert.Equal(("exitosa", "manual", 1), (despues!.Ultima!.Resultado, despues.Ultima.Disparo, despues.Ultima.Actualizadas));
        await using var plataforma = entorno.Plataforma(cadena);
        var evento = await plataforma.Auditoria.SingleAsync(a => a.Accion == SincronizadorPersonas.AccionAuditoria, Ct);
        Assert.Equal((TipoActor.Persona, admin.PersonaId.ToString()), (evento.ActorTipo, evento.ActorId));
    }

    [Fact]
    public async Task Sin_Entra_sincronizar_ahora_responde_409()
    {
        await using var api = entorno.Api();
        using var navegador = Navegador.De(api);
        navegador.Ip = IpNueva();
        var correo = $"admin.{Guid.NewGuid():N}@ejemplo.com";
        Assert.Equal(HttpStatusCode.NoContent, (await navegador.EntrarSimuladoAsync(correo, "Admin Demo", await entorno.GrupoAdministradorAsync())).StatusCode);

        var respuesta = await navegador.EnviarAsync(HttpMethod.Post, "/api/v1/plataforma/directorio/sincronizacion");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Equal("sincronizacion_no_disponible", (await respuesta.Content.ReadFromJsonAsync<ErrorSincronizacion>(Ct))!.Error);
        var vista = await (await navegador.GetAsync("/api/v1/plataforma/directorio/sincronizacion")).Content.ReadFromJsonAsync<VistaSincronizacion>(Ct);
        Assert.Equal((false, (DateTimeOffset?)null), (vista!.Disponible, vista.Proxima));
    }
}
