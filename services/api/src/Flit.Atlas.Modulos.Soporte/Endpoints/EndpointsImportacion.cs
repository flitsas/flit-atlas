using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Modulos.Soporte.Datos;
using Flit.Atlas.Modulos.Soporte.Dominio;
using Flit.Atlas.Modulos.Soporte.Dominio.Importacion;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Flit.Atlas.Plataforma.Permisos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using static Flit.Atlas.Modulos.Soporte.Endpoints.Comunes;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

/// <summary>
/// Importador del Excel de la rotación (HU 2.7, prompt maestro §8.3): vista previa y confirmación, con
/// <c>atlas.soporte.gestionar</c> global o con alcance al equipo destino. Como la generación (HU 2.8), la confirmación
/// recalcula el plan con el mismo archivo y las mismas decisiones y lo compara con la huella de la vista previa: nada se
/// guarda distinto de lo que se vio.
/// <para>
/// Reimportar no duplica: la clave natural de un turno importado es equipo + fecha + persona con origen
/// <c>importado</c>; si ya existe, la fila queda <c>ya_importado</c>. Una persona del Excel que no existe en Atlas se
/// propone crear <b>inactiva</b> (no entra en la generación) y quien importa la confirma.
/// </para>
/// </summary>
internal static class EndpointsImportacion
{
    /// <summary>Base64 de <see cref="LectorExcelRotacion.TamanoMaximo"/> bytes (4/3, redondeado hacia arriba).</summary>
    public const int LargoMaximoBase64 = ((LectorExcelRotacion.TamanoMaximo + 2) / 3) * 4;

    /// <summary>Tope del cuerpo: el archivo en base64 más las decisiones.</summary>
    private const long CuerpoMaximo = LargoMaximoBase64 + (64 * 1024);

    private const string SinPermiso = "No tienes atlas.soporte.gestionar sobre el equipo destino.";

    private sealed class LimiteCuerpo : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => CuerpoMaximo;
    }

    public static void Mapear(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/importacion/vista-previa", PrevisualizarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithMetadata(new LimiteCuerpo())
            .WithName("PrevisualizarImportacionSoporte")
            .WithSummary("Vista previa de la importación del Excel de la rotación")
            .WithDescription(
                $"Lee el .xlsx (máximo {LectorExcelRotacion.TamanoMaximo / 1024} KB y {LectorExcelRotacion.FilasMaximas} filas) y muestra " +
                "qué haría la importación en el equipo destino: nombres normalizados a personas (sin tildes ni mayúsculas, " +
                "nombres parciales únicos), fechas en texto y seriales de Excel, la columna Compensado interpretada " +
                "(fecha → compensación; «reemplazo» → turno reemplazado; lo ambiguo, a la observación para revisión), las " +
                "personas que no existen en Atlas (se proponen inactivas) y las filas ya importadas. No guarda nada. " +
                "Requiere atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<VistaPreviaImportacionDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .ProducesValidationProblem();

        grupo.MapPost("/importacion/confirmar", ConfirmarAsync)
            .RequierePermiso(PermisosSoporte.Gestionar)
            .WithMetadata(new LimiteCuerpo())
            .WithName("ConfirmarImportacionSoporte")
            .WithSummary("Importar el Excel de la rotación")
            .WithDescription(
                "Aplica la vista previa con el mismo archivo, las mismas decisiones y su huella (409 si algo cambió). Crea los " +
                "turnos con origen importado, las compensaciones, las personas inactivas confirmadas y los participantes. " +
                "Reimportar no duplica (equipo + fecha + persona). Queda en la auditoría, sin nombres. Requiere " +
                "atlas.soporte.gestionar global o con alcance al equipo.")
            .Produces<ResultadoImportacionDto>()
            .Produces<ErrorSoporte>(StatusCodes.Status403Forbidden)
            .Produces<ErrorSoporte>(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }

    /// <summary>Lo que hará la importación de una fila.</summary>
    private enum Accion
    {
        Crear,
        YaImportado,
        Omitir,
    }

    private sealed record FilaPlan(FilaInterpretada Fila, Accion Accion, string? Motivo);

    private sealed record Plan(
        EquipoSoporte Equipo, string Hoja, PlanRotacion Rotacion, List<FilaPlan> Filas, List<Turno> GeneradosQuitar,
        List<(string Clave, Guid? PersonaId, string Nombre, bool Activo)> ParticipantesNuevos, List<string> Avisos, string Huella,
        string HashArchivo);

    private static async Task<(Plan? Plan, IResult? Error)> PlanificarAsync(
        SolicitudImportacion solicitud, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        var equipoId = solicitud.EquipoId ?? SoporteDbContext.EquipoDesarrolloTi;
        var equipo = await soporte.Equipos.AsNoTracking().FirstOrDefaultAsync(e => e.Id == equipoId, ct);
        if (equipo is null)
        {
            return (null, Invalido("equipoId", "El equipo destino no existe."));
        }
        if (string.IsNullOrWhiteSpace(solicitud.Contenido))
        {
            return (null, Invalido("contenido", "Adjunta el archivo .xlsx de la rotación."));
        }
        if (solicitud.Contenido.Length > LargoMaximoBase64)
        {
            return (null, Invalido("contenido", $"El archivo pesa más de {LectorExcelRotacion.TamanoMaximo / 1024} KB."));
        }
        var bytes = new byte[(solicitud.Contenido.Length * 3 / 4) + 3];
        if (!Convert.TryFromBase64String(solicitud.Contenido.Trim(), bytes, out var largo))
        {
            return (null, Invalido("contenido", "El contenido no es base64 válido."));
        }
        Array.Resize(ref bytes, largo);
        HojaRotacion hoja;
        try
        {
            hoja = LectorExcelRotacion.Leer(bytes);
        }
        catch (LecturaExcelException error)
        {
            return (null, Invalido("contenido", error.Message));
        }
        if (hoja.Filas.Count == 0)
        {
            return (null, Invalido("contenido", "La hoja no tiene filas de datos."));
        }

        var crear = (solicitud.CrearInactivas ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(NombresPersona.Normalizar).ToHashSet(StringComparer.Ordinal);
        var elegidas = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var eleccion in solicitud.Elegidas ?? [])
        {
            if (!string.IsNullOrWhiteSpace(eleccion.Clave))
            {
                elegidas[NombresPersona.Normalizar(eleccion.Clave)] = eleccion.PersonaId;
            }
        }
        var personas = await plataforma.Personas.AsNoTracking().Where(p => p.Tipo == TipoPersona.Empleado)
            .OrderBy(p => p.Id).Select(p => new PersonaConocida(p.Id, p.Nombre, p.Estado == EstadoPersona.Activa)).ToListAsync(ct);
        var rotacion = InterpreteRotacion.Interpretar(hoja.Filas, personas, CalendarioBogota.Hoy(reloj), crear, elegidas);

        var fechas = rotacion.Filas.Where(f => f.Fecha is not null).Select(f => f.Fecha!.Value).Distinct().ToList();
        var existentes = await soporte.Turnos.AsNoTracking().Where(t => t.EquipoId == equipo.Id && fechas.Contains(t.Fecha)).ToListAsync(ct);
        var filas = new List<FilaPlan>();
        var quitar = new List<Turno>();
        var nuevosPorFecha = new Dictionary<DateOnly, int>();
        foreach (var fila in rotacion.Filas)
        {
            if (fila.Omitida is not null)
            {
                filas.Add(new FilaPlan(fila, Accion.Omitir, fila.Omitida));
                continue;
            }
            var fecha = fila.Fecha!.Value;
            var responsable = fila.Responsable!.Persona?.Id;
            var reemplazo = fila.Reemplazo?.Persona?.Id;
            var importados = existentes.Where(t => t.Fecha == fecha && t.Origen == OrigenTurno.Importado).ToList();
            if (responsable is { } r && importados.Any(t => t.PersonaId == r))
            {
                filas.Add(new FilaPlan(fila, Accion.YaImportado, "La fila ya se importó antes: no se duplica."));
                continue;
            }
            // Quién queda de turno con esta fila (el reemplazo o el responsable), si queda alguien.
            var quedaVigente = ReglasTurno.Vigentes.Contains(fila.Estado);
            var vigente = quedaVigente ? reemplazo ?? responsable : null;
            var delDia = existentes.Where(t => t.Fecha == fecha && ReglasTurno.Vigentes.Contains(t.Estado)).ToList();
            // Los generados que nadie ha confirmado se quitan: el Excel manda. Los demás se respetan.
            var generados = delDia.Where(t => t.Origen == OrigenTurno.Generado && t.Estado == EstadoTurno.Programado).ToList();
            var fijos = delDia.Except(generados).ToList();
            if (vigente is { } v && fijos.FirstOrDefault(t => t.PersonaId == v) is { } igual)
            {
                filas.Add(new FilaPlan(fila, Accion.Omitir,
                    $"Ya hay un turno {ReglasTurno.Texto(igual.Origen)} de esa persona ese sábado en el equipo: no se duplica."));
                continue;
            }
            if (quedaVigente && fijos.Count + nuevosPorFecha.GetValueOrDefault(fecha) >= equipo.PersonasMaximo)
            {
                filas.Add(new FilaPlan(fila, Accion.Omitir,
                    $"El equipo ya tiene {equipo.PersonasMaximo} {(equipo.PersonasMaximo == 1 ? "persona" : "personas")} ese sábado con turnos que no son del Excel."));
                continue;
            }
            foreach (var generado in generados.Where(g => !quitar.Contains(g)))
            {
                quitar.Add(generado);
                fila.Interpretaciones.Add("Se quita el turno generado (programado) que había ese sábado: el Excel manda.");
            }
            if (quedaVigente)
            {
                nuevosPorFecha[fecha] = nuevosPorFecha.GetValueOrDefault(fecha) + 1;
            }
            filas.Add(new FilaPlan(fila, Accion.Crear, null));
        }

        // Participantes: los responsables de las filas que se importan y aún no están en el equipo, en el orden del Excel.
        var participantes = new List<(string, Guid?, string, bool)>();
        if (solicitud.AgregarParticipantes != false)
        {
            var yaEstan = await soporte.Participantes.AsNoTracking().Where(p => p.EquipoId == equipo.Id).Select(p => p.PersonaId).ToListAsync(ct);
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in filas.Where(f => f.Accion != Accion.Omitir).OrderBy(f => f.Fila.Fecha).ThenBy(f => f.Fila.Numero))
            {
                var n = f.Fila.Responsable!;
                if (!vistos.Add(n.Clave) || (n.Persona is { } p && yaEstan.Contains(p.Id)))
                {
                    continue;
                }
                participantes.Add((n.Clave, n.Persona?.Id, n.Persona?.Nombre ?? n.Texto, n.Persona?.Activa == true));
            }
        }

        var avisos = new List<string>();
        if (rotacion.PersonasNuevas.Any(p => !p.Confirmada))
        {
            avisos.Add("Hay personas del Excel que no existen en Atlas: confirma crearlas inactivas o sus filas no se importan.");
        }
        if (rotacion.NombresAmbiguos.Any(a => a.Elegida is null))
        {
            avisos.Add("Hay nombres que encajan con varias personas: elige cuál es cada uno.");
        }
        var hashArchivo = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var huella = Huella(equipo, hashArchivo, filas, quitar, participantes, rotacion);
        return (new Plan(equipo, hoja.Hoja, rotacion, filas, quitar, participantes, avisos, huella, hashArchivo), null);
    }

    /// <summary>
    /// SHA-256 del equipo, del archivo, de lo que hará cada fila, de los turnos generados que se quitan, de los participantes
    /// y de las decisiones de quien importa. Detecta que algo cambió entre la vista previa y la confirmación.
    /// </summary>
    private static string Huella(
        EquipoSoporte equipo, string hashArchivo, List<FilaPlan> filas, List<Turno> quitar,
        List<(string Clave, Guid? PersonaId, string Nombre, bool Activo)> participantes, PlanRotacion rotacion)
    {
        var texto = new StringBuilder();
        texto.Append(CultureInfo.InvariantCulture, $"{equipo.Id}|{equipo.PersonasMaximo}|{hashArchivo}\n");
        foreach (var (f, accion, _) in filas)
        {
            var comp = f.Compensacion is { } c ? $"{c.ParaReemplazo}:{c.Fecha:O}:{c.Estado}" : "-";
            texto.Append(CultureInfo.InvariantCulture,
                $"{f.Numero}|{accion}|{f.Fecha:O}|{f.Responsable?.Persona?.Id}|{f.Responsable?.CrearInactiva}|{f.Estado}|{f.Reemplazo?.Persona?.Id}|{comp}|{f.RequiereRevision}|{f.Observacion?.Length}\n");
        }
        foreach (var t in quitar)
        {
            texto.Append(CultureInfo.InvariantCulture, $"q|{t.Id}\n");
        }
        foreach (var p in participantes)
        {
            texto.Append(CultureInfo.InvariantCulture, $"p|{p.Clave}|{p.PersonaId}|{p.Activo}\n");
        }
        foreach (var n in rotacion.PersonasNuevas)
        {
            texto.Append(CultureInfo.InvariantCulture, $"n|{n.Clave}|{n.Confirmada}\n");
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto.ToString())));
    }

    private static string TextoAccion(Accion accion) => accion switch
    {
        Accion.Crear => "crear",
        Accion.YaImportado => "ya_importado",
        _ => "omitir",
    };

    private static VistaPreviaImportacionDto Dto(Plan p)
    {
        var filas = p.Filas.Select(fp =>
        {
            var f = fp.Fila;
            var compensacion = f.Compensacion is { } c
                ? new CompensacionImportacionDto(c.ParaReemplazo ? "reemplazo" : "responsable", c.Fecha, ReglasTurno.Texto(c.Estado))
                : null;
            return new FilaImportacionDto(f.Numero, f.Fecha, f.TextoFecha, f.Responsable is { } r ? TextosImportacion.Dto(r) : null,
                ReglasTurno.Texto(f.Estado), f.Reemplazo is { } q ? TextosImportacion.Dto(q) : null, compensacion, f.Observacion,
                fp.Accion != Accion.Omitir && f.RequiereRevision, TextoAccion(fp.Accion), fp.Motivo, f.Interpretaciones, f.Ambiguedades);
        }).ToList();
        var crear = p.Filas.Where(f => f.Accion == Accion.Crear).ToList();
        var resumen = new ResumenImportacionDto(
            p.Filas.Count,
            crear.Count,
            p.Filas.Count(f => f.Accion == Accion.YaImportado),
            p.Filas.Count(f => f.Accion == Accion.Omitir),
            crear.Sum(f => f.Fila.Reemplazo is null ? 1 : 2),
            crear.Count(f => f.Fila.Compensacion is not null),
            PersonasACrear(p).Count,
            p.ParticipantesNuevos.Count,
            p.GeneradosQuitar.Count,
            p.Filas.Sum(f => f.Fila.Interpretaciones.Count),
            p.Filas.Count(f => f.Accion != Accion.Omitir && f.Fila.RequiereRevision));
        return new VistaPreviaImportacionDto(
            new EquipoResumenDto(p.Equipo.Id, p.Equipo.Nombre), p.Hoja, p.Huella, resumen, filas,
            [.. p.Rotacion.PersonasNuevas.Select(n => new PersonaNuevaDto(n.Clave, n.Nombre, n.Filas, n.UltimaFecha, n.Confirmada))],
            [.. p.Rotacion.NombresAmbiguos.Select(a => new NombreAmbiguoDto(a.Clave, a.Texto,
                [.. a.Candidatas.Select(c => new CandidataDto(c.Id, c.Nombre, c.Activa))], a.Elegida))],
            [.. p.ParticipantesNuevos.Select(n => new ParticipanteNuevoDto(n.PersonaId, n.Nombre, n.Activo))],
            p.Avisos);
    }

    /// <summary>Personas nuevas confirmadas que usa alguna fila que se importa (clave → nombre del Excel).</summary>
    private static Dictionary<string, string> PersonasACrear(Plan p)
    {
        var usadas = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in p.Filas.Where(f => f.Accion == Accion.Crear && f.Fila.Responsable!.CrearInactiva))
        {
            usadas.TryAdd(f.Fila.Responsable!.Clave, f.Fila.Responsable.Texto);
        }
        return usadas;
    }

    private static async Task<IResult> PrevisualizarAsync(
        [Description("El archivo en base64, el equipo destino y las decisiones de quien importa.")] SolicitudImportacion solicitud,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, solicitud.EquipoId ?? SoporteDbContext.EquipoDesarrolloTi, ct))
        {
            return SinPermiso(SinPermiso);
        }
        var (plan, error) = await PlanificarAsync(solicitud, soporte, plataforma, reloj, ct);
        return error ?? Results.Ok(Dto(plan!));
    }

    private static async Task<IResult> ConfirmarAsync(
        [Description("Lo mismo que la vista previa, más su huella.")] SolicitudImportacion solicitud,
        HttpContext http, IPermisosDeSesion permisos, SoporteDbContext soporte, PlataformaDbContext plataforma, TimeProvider reloj, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(solicitud.Huella))
        {
            return Invalido("huella", "Envía la huella de la vista previa que confirmas.");
        }
        if (!await PuedeEnEquipoAsync(permisos, http.User, PermisosSoporte.Gestionar, solicitud.EquipoId ?? SoporteDbContext.EquipoDesarrolloTi, ct))
        {
            return SinPermiso(SinPermiso);
        }
        var (plan, error) = await PlanificarAsync(solicitud, soporte, plataforma, reloj, ct);
        if (error is not null)
        {
            return error;
        }
        var p = plan!;
        if (!string.Equals(p.Huella, solicitud.Huella.Trim(), StringComparison.Ordinal))
        {
            return Conflicto("vista_previa_vencida", "Algo cambió desde la vista previa: vuelve a generarla y revísala antes de importar.");
        }

        var actor = PersonaDeSesion(http.User);
        var configuracion = await ConfiguracionAsync(soporte, ct);
        var (horaInicio, horaFin) = HorarioDe(p.Equipo, configuracion);

        // Personas nuevas (inactivas): solo las confirmadas que usa alguna fila. Correo de relleno en un dominio reservado
        // (RFC 2606), único, que nunca coincide con una cuenta de Entra.
        var organizacion = await plataforma.Organizaciones.Where(o => o.Tipo == TipoOrganizacion.Interna).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct)
            ?? OrganizacionDeSesion(http.User);
        if (organizacion is null)
        {
            return Conflicto("sin_organizacion_interna", "No hay organización interna para crear las personas nuevas.");
        }
        var nuevas = PersonasACrear(p).Select(kv =>
        {
            var id = Guid.CreateVersion7();
            var nombre = kv.Value.Length > 200 ? kv.Value[..200] : kv.Value;
            return (Clave: kv.Key, Persona: new Persona
            {
                Id = id,
                Tipo = TipoPersona.Empleado,
                OrganizacionId = organizacion.Value,
                Nombre = nombre,
                Correo = $"soporte.importado.{id:N}@personas.invalid",
                Estado = EstadoPersona.Inactiva,
            });
        }).ToList();
        var porClave = nuevas.ToDictionary(n => n.Clave, n => n.Persona.Id, StringComparer.Ordinal);
        Guid PersonaDe(NombreResuelto n) => n.Persona?.Id ?? porClave[n.Clave];

        var idsQuitar = p.GeneradosQuitar.Select(t => t.Id).ToList();
        var quitar = await soporte.Turnos.Where(t => idsQuitar.Contains(t.Id)).ToListAsync(ct);
        var nuevosTurnos = new List<Turno>();
        var nuevasCompensaciones = new List<Compensacion>();
        var nuevosParticipantes = new List<Participante>();
        var turnos = 0;
        var compensaciones = 0;
        foreach (var (f, _, _) in p.Filas.Where(f => f.Accion == Accion.Crear))
        {
            var original = new Turno
            {
                EquipoId = p.Equipo.Id,
                Fecha = f.Fecha!.Value,
                PersonaId = PersonaDe(f.Responsable!),
                HoraInicio = horaInicio,
                HoraFin = horaFin,
                Estado = f.Reemplazo is null ? f.Estado : EstadoTurno.Reemplazado,
                Origen = OrigenTurno.Importado,
                Observacion = f.Observacion,
                RequiereRevision = f.RequiereRevision,
                CreadoPorPersonaId = actor,
            };
            nuevosTurnos.Add(original);
            turnos++;
            var compensado = original;
            if (f.Reemplazo is { } reemplazo)
            {
                compensado = new Turno
                {
                    EquipoId = p.Equipo.Id,
                    Fecha = original.Fecha,
                    PersonaId = PersonaDe(reemplazo),
                    HoraInicio = horaInicio,
                    HoraFin = horaFin,
                    Estado = f.Estado,
                    Origen = OrigenTurno.Importado,
                    TurnoReemplazadoId = original.Id,
                    RequiereRevision = f.RequiereRevision,
                    CreadoPorPersonaId = actor,
                };
                nuevosTurnos.Add(compensado);
                turnos++;
            }
            if (f.Compensacion is { } c)
            {
                nuevasCompensaciones.Add(new Compensacion
                {
                    TurnoId = c.ParaReemplazo ? compensado.Id : original.Id,
                    PersonaId = c.ParaReemplazo ? compensado.PersonaId : original.PersonaId,
                    FechaCompensada = c.Fecha,
                    Estado = c.Estado,
                    Nota = $"Importada del Excel (fila {f.Numero}).",
                });
                compensaciones++;
            }
        }
        var orden = await soporte.Participantes.Where(x => x.EquipoId == p.Equipo.Id).MaxAsync(x => (int?)x.Orden, ct) ?? 0;
        foreach (var n in p.ParticipantesNuevos)
        {
            nuevosParticipantes.Add(new Participante
            {
                EquipoId = p.Equipo.Id,
                PersonaId = n.PersonaId ?? porClave[n.Clave],
                Orden = ++orden,
                Activo = n.Activo,
            });
        }

        var importacion = Guid.CreateVersion7();
        var dto = Dto(p);
        try
        {
            await GuardarAsync(soporte, plataforma,
                [Evento(http, reloj, "importacion_confirmada", "soporte_importacion", importacion, new
                {
                    equipoId = p.Equipo.Id,
                    archivo = p.HashArchivo,
                    filas = dto.Resumen.Filas,
                    importadas = dto.Resumen.Importadas,
                    yaImportadas = dto.Resumen.YaImportadas,
                    omitidas = dto.Resumen.Omitidas,
                    turnos,
                    compensaciones,
                    personasInactivas = nuevas.Count,
                    participantes = p.ParticipantesNuevos.Count,
                    generadosQuitados = idsQuitar.Count,
                    interpretaciones = dto.Resumen.Interpretaciones,
                    ambiguos = dto.Resumen.Ambiguos,
                })], ct,
                antes: async () =>
                {
                    // Primero las personas (los turnos las referencian) y los generados que se quitan (índice único de un
                    // turno vigente por persona); después, lo nuevo.
                    if (nuevas.Count > 0)
                    {
                        plataforma.Personas.AddRange(nuevas.Select(n => n.Persona));
                        await plataforma.SaveChangesAsync(ct);
                    }
                    soporte.Turnos.RemoveRange(quitar);
                    await soporte.SaveChangesAsync(ct);
                    soporte.Turnos.AddRange(nuevosTurnos);
                    soporte.Compensaciones.AddRange(nuevasCompensaciones);
                    soporte.Participantes.AddRange(nuevosParticipantes);
                });
        }
        catch (DbUpdateException repetido) when (EsTurnoRepetido(repetido))
        {
            return Conflicto("vista_previa_vencida", "Otro cambio llegó al mismo tiempo: vuelve a generar la vista previa.");
        }
        return Results.Ok(new ResultadoImportacionDto(importacion, dto));
    }
}
