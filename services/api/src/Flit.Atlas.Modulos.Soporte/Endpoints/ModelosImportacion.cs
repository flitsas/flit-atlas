using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Flit.Atlas.Modulos.Soporte.Dominio.Importacion;

namespace Flit.Atlas.Modulos.Soporte.Endpoints;

// Contrato del importador del Excel de la rotación (HU 2.7, ATLAS-ADR-0029).

[Description("Persona elegida para un nombre del Excel que encaja con varias personas.")]
public sealed record EleccionPersona(
    [property: Description("Nombre normalizado del Excel (la clave que devolvió la vista previa)."), MaxLength(200)] string? Clave,
    [property: Description("Persona elegida entre las candidatas.")] Guid PersonaId);

[Description(
    "El .xlsx de la rotación (hoja «Rotacion Soporte»: Fecha, Responsable, Estado, Compensado, Observación) en base64 y " +
    "las decisiones de quien importa. La vista previa y la confirmación reciben lo mismo; la confirmación, además, la huella.")]
public sealed record SolicitudImportacion(
    [property: Description("Equipo destino; vacío, Desarrollo TI (el del Excel).")] Guid? EquipoId,
    [property: Description("Nombre del archivo, solo para el informe."), MaxLength(200)] string? NombreArchivo,
    [property: Description("Contenido del .xlsx en base64 (máximo 1 MB antes de codificar)."), MaxLength(EndpointsImportacion.LargoMaximoBase64)] string? Contenido,
    [property: Description("Claves de las personas que no existen en Atlas y que quien importa confirma crear como inactivas."), MaxLength(100)] IReadOnlyList<string>? CrearInactivas,
    [property: Description("Persona elegida para cada nombre ambiguo."), MaxLength(100)] IReadOnlyList<EleccionPersona>? Elegidas,
    [property: Description("Si agrega al equipo a los responsables que aún no están en su rotación (activos o, los nuevos, inactivos); vacío, sí.")] bool? AgregarParticipantes,
    [property: Description("Solo al confirmar: la huella de la vista previa que se aplica."), MaxLength(64)] string? Huella);

[Description("Nombre del Excel y la persona de Atlas que le corresponde.")]
public sealed record NombreImportacionDto(
    [property: Description("Texto del Excel.")] string Texto,
    [property: Description("Nombre normalizado (sin tildes ni mayúsculas).")] string Clave,
    [property: Description("exacta, parcial, ambigua o ninguna.")] string Coincidencia,
    [property: Description("Persona de Atlas; vacía si es ambigua sin elegir o si no existe.")] Guid? PersonaId,
    [property: Description("Nombre de la persona en Atlas.")] string? Nombre,
    [property: Description("Persona nueva que se crea inactiva al confirmar.")] bool Nueva);

[Description("Compensación leída de la columna «Compensado».")]
public sealed record CompensacionImportacionDto(
    [property: Description("responsable o reemplazo: a quién se compensa.")] string Para,
    [property: Description("Día compensado; vacío si el Excel no lo dice.")] DateOnly? Fecha,
    [property: Description("pendiente o tomada.")] string Estado);

[Description("Una fila del Excel, lo que se entendió de ella y lo que hará la importación.")]
public sealed record FilaImportacionDto(
    [property: Description("Número de la fila en Excel.")] int Fila,
    [property: Description("Sábado del turno, si la fecha se pudo leer.")] DateOnly? Fecha,
    [property: Description("Valor original de la celda de la fecha.")] string? TextoFecha,
    [property: Description("Responsable de la fila.")] NombreImportacionDto? Responsable,
    [property: Description("Estado del turno que queda vigente (el del reemplazo, si lo hubo).")] string Estado,
    [property: Description("Quien tomó el turno, si la columna «Compensado» habla de un reemplazo.")] NombreImportacionDto? Reemplazo,
    [property: Description("Compensación, si la hay.")] CompensacionImportacionDto? Compensacion,
    [property: Description("Observación que queda en el turno.")] string? Observacion,
    [property: Description("Si el turno queda marcado para revisión humana.")] bool RequiereRevision,
    [property: Description("crear (se importa), ya_importado (no se duplica) u omitir.")] string Accion,
    [property: Description("Por qué se omite o por qué no se duplica.")] string? Motivo,
    [property: Description("Cada interpretación hecha en la fila.")] IReadOnlyList<string> Interpretaciones,
    [property: Description("Cada caso ambiguo de la fila (queda en la observación, para revisión).")] IReadOnlyList<string> Ambiguedades);

[Description("Persona del Excel que no existe en Atlas: se propone crearla inactiva (no entra en la generación).")]
public sealed record PersonaNuevaDto(
    [property: Description("Clave para confirmarla en crearInactivas.")] string Clave,
    [property: Description("Nombre como viene en el Excel.")] string Nombre,
    [property: Description("Filas en las que es responsable.")] int Filas,
    [property: Description("Su último turno en el Excel.")] DateOnly? UltimaFecha,
    [property: Description("Si quien importa ya confirmó crearla.")] bool Confirmada);

[Description("Persona candidata para un nombre ambiguo.")]
public sealed record CandidataDto(
    [property: Description("Id de la persona.")] Guid Id,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Si está activa en Atlas.")] bool Activa);

[Description("Nombre del Excel que encaja con varias personas: quien importa elige una en elegidas.")]
public sealed record NombreAmbiguoDto(
    [property: Description("Clave para elegirla.")] string Clave,
    [property: Description("Texto del Excel.")] string Texto,
    [property: Description("Personas que encajan.")] IReadOnlyList<CandidataDto> Candidatas,
    [property: Description("La elegida, si ya se eligió.")] Guid? Elegida);

[Description("Persona que se agrega a la rotación del equipo al confirmar.")]
public sealed record ParticipanteNuevoDto(
    [property: Description("Id de la persona; vacío si es una persona nueva.")] Guid? PersonaId,
    [property: Description("Nombre.")] string Nombre,
    [property: Description("Si entra activa (los nuevos y las personas inactivas entran inactivos).")] bool Activo);

[Description("Conteos de la vista previa o de la importación.")]
public sealed record ResumenImportacionDto(
    [property: Description("Filas de datos leídas.")] int Filas,
    [property: Description("Filas que se importan (o se importaron).")] int Importadas,
    [property: Description("Filas que ya estaban importadas: no se duplican.")] int YaImportadas,
    [property: Description("Filas omitidas.")] int Omitidas,
    [property: Description("Turnos que se crean (un reemplazo crea dos: el reemplazado y el del reemplazo).")] int Turnos,
    [property: Description("Compensaciones que se crean.")] int Compensaciones,
    [property: Description("Personas inactivas que se crean.")] int PersonasInactivas,
    [property: Description("Participantes que se agregan al equipo.")] int Participantes,
    [property: Description("Turnos generados (programados) que se quitan porque el Excel trae otra persona ese sábado.")] int GeneradosQuitados,
    [property: Description("Interpretaciones hechas.")] int Interpretaciones,
    [property: Description("Casos ambiguos (filas marcadas para revisión).")] int Ambiguos);

[Description("Vista previa de la importación: nada se guarda hasta confirmar con la misma huella.")]
public sealed record VistaPreviaImportacionDto(
    [property: Description("Equipo destino.")] EquipoResumenDto Equipo,
    [property: Description("Hoja leída.")] string Hoja,
    [property: Description("Huella para confirmar exactamente esta vista previa.")] string Huella,
    [property: Description("Conteos.")] ResumenImportacionDto Resumen,
    [property: Description("Cada fila con sus interpretaciones y casos ambiguos (el informe).")] IReadOnlyList<FilaImportacionDto> Filas,
    [property: Description("Personas que no existen en Atlas.")] IReadOnlyList<PersonaNuevaDto> PersonasNuevas,
    [property: Description("Nombres que encajan con varias personas.")] IReadOnlyList<NombreAmbiguoDto> NombresAmbiguos,
    [property: Description("Personas que entran a la rotación del equipo.")] IReadOnlyList<ParticipanteNuevoDto> ParticipantesNuevos,
    [property: Description("Avisos para quien importa.")] IReadOnlyList<string> Avisos);

[Description("Resultado de la importación, con el informe final.")]
public sealed record ResultadoImportacionDto(
    [property: Description("Id de la importación (en la auditoría).")] Guid ImportacionId,
    [property: Description("El informe: lo mismo que la vista previa confirmada.")] VistaPreviaImportacionDto Informe);

internal static class TextosImportacion
{
    public static string Coincidencia(Dominio.TipoCoincidencia tipo) => Dominio.ReglasTurno.Texto(tipo);

    public static NombreImportacionDto Dto(NombreResuelto n) => new(
        n.Texto, n.Clave, Coincidencia(n.Tipo), n.Persona?.Id, n.Persona?.Nombre, n.CrearInactiva);
}
