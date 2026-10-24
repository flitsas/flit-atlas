using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

[Description("Error del módulo de equipo con un código estable.")]
public sealed record ErrorEquipo(
    [property: Description("Código: no_encontrado, version_no_valida, hay_pendientes, vista_previa_cambio, sin_reparto, checklist_incompleto, no_es_secundario, ya_habilitado, version_repetida.")] string Codigo,
    [property: Description("Qué pasó, en español.")] string Mensaje);

[Description("Aplicativo del catálogo, para enlazar su ficha.")]
public sealed record AplicativoRefDto(
    [property: Description("Id del aplicativo en el catálogo.")] Guid Id,
    [property: Description("Slug del aplicativo: es la URL de su ficha.")] string Slug,
    [property: Description("Nombre del aplicativo.")] string Nombre,
    [property: Description("producto, modulo o servicio.")] string Tipo);

[Description("Persona del equipo. El teléfono solo llega con atlas.equipo.ver_contacto.")]
public sealed record PersonaEquipoDto(
    [property: Description("Id de la persona.")] Guid Id,
    [property: Description("Nombre completo.")] string Nombre,
    [property: Description("Correo corporativo.")] string? Correo,
    [property: Description("Cargo, de Microsoft 365.")] string? Cargo,
    [property: Description("URL de la foto; vacía sin foto.")] string? Foto,
    [property: Description("Teléfono; vacío sin atlas.equipo.ver_contacto.")] string? Telefono,
    [property: Description("Usuario de GitHub, para CODEOWNERS.")] string? UsuarioGitHub,
    [property: Description("Si la persona está activa en Atlas.")] bool Activa);

[Description("Un responsable de un aplicativo: la persona, su tipo, su dedicación, su vigencia y su traspaso.")]
public sealed record ResponsableDto(
    [property: Description("Id de la responsabilidad.")] Guid Id,
    [property: Description("principal o secundario.")] string Tipo,
    [property: Description("La persona responsable.")] PersonaEquipoDto Persona,
    [property: Description("Dedicación orientativa en porcentaje (el secundario ≈ 20 %); vacía si no se definió.")] int? Dedicacion,
    [property: Description("Desde cuándo (aaaa-mm-dd).")] DateOnly VigenteDesde,
    [property: Description("Hasta cuándo, incluido; vacío sin fin.")] DateOnly? VigenteHasta,
    [property: Description("Si está vigente hoy (y da el rol implícito de editor).")] bool Vigente,
    [property: Description("en_acompanamiento o habilitado.")] string EstadoTraspaso);

[Description("Un aplicativo con su principal y su secundario.")]
public sealed record FilaRepartoDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("Responsable principal; vacío si no tiene.")] ResponsableDto? Principal,
    [property: Description("Responsable secundario; vacío si no tiene.")] ResponsableDto? Secundario);

[Description("Una persona con los aplicativos de los que es principal y secundaria.")]
public sealed record PersonaRepartoDto(
    [property: Description("La persona.")] PersonaEquipoDto Persona,
    [property: Description("Aplicativos de los que es principal, en el orden del reparto.")] IReadOnlyList<AplicativoRefDto> PrincipalDe,
    [property: Description("Aplicativos de los que es secundaria, en el orden del reparto.")] IReadOnlyList<AplicativoRefDto> SecundarioDe);

[Description("Definición de un rol tal como está en el §1 del documento de responsables.")]
public sealed record DefinicionRolDto(
    [property: Description("principal o secundario.")] string Rol,
    [property: Description("Título del documento («Responsable principal»).")] string Titulo,
    [property: Description("Cada punto, sin cambiar una letra.")] IReadOnlyList<string> Puntos);

[Description("Una versión del reparto: número, origen, autor y nota.")]
public sealed record VersionResumenDto(
    [property: Description("Número de versión (la importación del documento crea la que dice el documento).")] int Numero,
    [property: Description("importacion, cambio o restauracion.")] string Origen,
    [property: Description("Versión restaurada, si es una restauración.")] int? RestauradaDe,
    [property: Description("Fecha del documento importado, si aplica.")] DateOnly? FechaDocumento,
    [property: Description("Quién definió el reparto según el documento, si aplica.")] string? DefinidoPor,
    [property: Description("Persona de Atlas que hizo el cambio.")] string? Autor,
    [property: Description("Nota del cambio.")] string Nota,
    [property: Description("Cuándo se creó la versión.")] DateTimeOffset CreadaEn,
    [property: Description("Huella del reparto (SHA-256): dos versiones iguales tienen la misma.")] string Huella,
    [property: Description("Cuántas responsabilidades tiene.")] int Responsabilidades);

[Description("El reparto vigente: definiciones de los roles, por persona, por producto, por módulo y por servicio.")]
public sealed record RepartoDto(
    [property: Description("Versión vigente; vacía si aún no hay reparto.")] VersionResumenDto? Version,
    [property: Description("Definiciones de cada rol (§1 del documento).")] IReadOnlyList<DefinicionRolDto> Definiciones,
    [property: Description("Reparto por persona (tabla 2).")] IReadOnlyList<PersonaRepartoDto> PorPersona,
    [property: Description("Productos (tabla 3.1).")] IReadOnlyList<FilaRepartoDto> Productos,
    [property: Description("Módulos (tabla 3.2).")] IReadOnlyList<FilaRepartoDto> Modulos,
    [property: Description("Servicios con responsables propios.")] IReadOnlyList<FilaRepartoDto> Servicios,
    [property: Description("Responsabilidades de aplicativos que ya no están en el catálogo.")] int Huerfanas,
    [property: Description("Si la persona ve teléfonos (atlas.equipo.ver_contacto).")] bool VeContacto,
    [property: Description("Si la persona puede cambiar el reparto (atlas.equipo.gestionar).")] bool PuedeGestionar);

[Description("Las tablas del documento reconstruidas desde los datos, en Markdown.")]
public sealed record TablasRepartoDto(
    [property: Description("Versión vigente.")] int? Version,
    [property: Description("Tabla 2: reparto por persona.")] string Personas,
    [property: Description("Tabla 3.1: productos.")] string Productos,
    [property: Description("Tabla 3.2: módulos.")] string Modulos,
    [property: Description("Servicios con responsables propios; vacío si no hay.")] string? Servicios,
    [property: Description("Las tablas con sus títulos, para copiar.")] string Completo);

[Description("Carga de una persona en el reparto.")]
public sealed record CargaPersonaDto(
    [property: Description("La persona.")] PersonaEquipoDto Persona,
    [property: Description("De cuántos aplicativos es principal.")] int Principales,
    [property: Description("De cuántos es secundaria.")] int Secundarias,
    [property: Description("Principales + la dedicación de cada secundaria (20 % = 0,2).")] decimal Puntaje,
    [property: Description("alta, normal o baja frente a la media.")] string Nivel);

[Description("Mapa de carga del equipo: resalta a quien se sale de la media (±30 %).")]
public sealed record MapaCargaDto(
    [property: Description("Puntaje medio.")] decimal Media,
    [property: Description("Por encima, la carga es alta.")] decimal UmbralAlto,
    [property: Description("Por debajo, la carga es baja.")] decimal UmbralBajo,
    [property: Description("Cada persona, de mayor a menor carga.")] IReadOnlyList<CargaPersonaDto> Personas);

[Description("Una responsabilidad vista desde la persona.")]
public sealed record ResponsabilidadDePersonaDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("La responsabilidad.")] ResponsableDto Responsable);

[Description("Responsabilidades de una persona: de qué es principal y de qué es secundaria.")]
public sealed record ResponsabilidadesPersonaDto(
    [property: Description("La persona.")] PersonaEquipoDto Persona,
    [property: Description("Principal de.")] IReadOnlyList<ResponsabilidadDePersonaDto> PrincipalDe,
    [property: Description("Secundaria de.")] IReadOnlyList<ResponsabilidadDePersonaDto> SecundarioDe);

[Description("Una actividad del traspaso acompañado.")]
public sealed record ActividadTraspasoDto(
    [property: Description("Id de la actividad.")] Guid Id,
    [property: Description("despliegue o guardia.")] string Tipo,
    [property: Description("Día de la actividad.")] DateOnly Fecha,
    [property: Description("Nota.")] string? Nota,
    [property: Description("Cuándo se registró.")] DateTimeOffset RegistradaEn);

[Description("Traspaso acompañado de un secundario: checklist de al menos un despliegue y una guardia con el principal.")]
public sealed record TraspasoDto(
    [property: Description("Id de la responsabilidad secundaria.")] Guid ResponsabilidadId,
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("El secundario.")] PersonaEquipoDto Secundario,
    [property: Description("El principal que acompaña; vacío si no hay.")] PersonaEquipoDto? Principal,
    [property: Description("en_acompanamiento o habilitado.")] string Estado,
    [property: Description("Despliegues hechos con el principal.")] int Despliegues,
    [property: Description("Guardias hechas con el principal.")] int Guardias,
    [property: Description("Si ya cumple el checklist (≥ 1 despliegue y ≥ 1 guardia).")] bool Completo,
    [property: Description("Actividades registradas, de la más reciente a la más antigua.")] IReadOnlyList<ActividadTraspasoDto> Actividades);

[Description("Responsables de un aplicativo: los propios o, si no tiene, los del ancestro más cercano.")]
public sealed record ResponsablesAplicativoDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("Principal.")] ResponsableDto? Principal,
    [property: Description("Secundario.")] ResponsableDto? Secundario,
    [property: Description("Si los responsables son de un ancestro (un servicio sin responsables propios).")] AplicativoRefDto? HeredadoDe,
    [property: Description("Definiciones de cada rol (§1 del documento).")] IReadOnlyList<DefinicionRolDto> Definiciones,
    [property: Description("Traspaso del secundario, si está en acompañamiento.")] TraspasoDto? Traspaso,
    [property: Description("Versión vigente del reparto.")] int? Version,
    [property: Description("Si la persona puede cambiar el reparto.")] bool PuedeGestionar,
    [property: Description("Si la persona ve teléfonos.")] bool VeContacto);

[Description("Un equipo de soporte con sus personas de turno.")]
public sealed record EquipoTurnoDto(
    [property: Description("Id del equipo de soporte.")] Guid Id,
    [property: Description("Nombre del equipo.")] string Nombre,
    [property: Description("Área del equipo.")] string? Area,
    [property: Description("Hora de inicio (HH:mm, Bogotá).")] string? HoraInicio,
    [property: Description("Hora de fin (HH:mm, Bogotá).")] string? HoraFin,
    [property: Description("Personas de turno; vacío si nadie.")] IReadOnlyList<PersonaEquipoDto> Personas);

[Description("Soporte de turno de hoy, por equipo.")]
public sealed record SoporteHoyDto(
    [property: Description("Hoy (aaaa-mm-dd).")] DateOnly Fecha,
    [property: Description("Nombre del festivo, si hoy lo es.")] string? Festivo,
    [property: Description("Equipos activos con sus personas de turno.")] IReadOnlyList<EquipoTurnoDto> Equipos);

[Description("«¿A quién llamo?»: soporte de turno si hoy es sábado, y el principal y el secundario del aplicativo.")]
public sealed record AQuienLlamoDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("Hoy en Bogotá.")] DateOnly Hoy,
    [property: Description("Si hoy es sábado.")] bool EsSabado,
    [property: Description("Soporte de turno de hoy; vacío si no es sábado o no hay módulo de soporte.")] SoporteHoyDto? Soporte,
    [property: Description("Principal.")] ResponsableDto? Principal,
    [property: Description("Secundario.")] ResponsableDto? Secundario,
    [property: Description("Si los responsables son de un ancestro.")] AplicativoRefDto? HeredadoDe,
    [property: Description("Si la persona ve teléfonos (atlas.equipo.ver_contacto).")] bool VeContacto);

[Description("Una responsabilidad dentro de una versión.")]
public sealed record ResponsabilidadVersionDto(
    [property: Description("El aplicativo (nombre del momento de la versión).")] AplicativoRefDto Aplicativo,
    [property: Description("principal o secundario.")] string Tipo,
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Nombre de la persona en esa versión.")] string Persona,
    [property: Description("Dedicación orientativa.")] int? Dedicacion,
    [property: Description("Desde cuándo.")] DateOnly VigenteDesde,
    [property: Description("Hasta cuándo.")] DateOnly? VigenteHasta);

[Description("Una versión del reparto con su foto completa y sus tablas.")]
public sealed record VersionDetalleDto(
    [property: Description("La versión.")] VersionResumenDto Resumen,
    [property: Description("Definiciones de cada rol en esa versión.")] IReadOnlyList<DefinicionRolDto> Definiciones,
    [property: Description("Sus responsabilidades.")] IReadOnlyList<ResponsabilidadVersionDto> Responsabilidades,
    [property: Description("Sus tablas en Markdown.")] TablasRepartoDto Tablas);

[Description("Un cambio entre dos versiones.")]
public sealed record CambioVersionDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("principal o secundario.")] string Tipo,
    [property: Description("agregada, retirada, persona o datos.")] string Cambio,
    [property: Description("Persona antes.")] string? Antes,
    [property: Description("Persona después.")] string? Despues,
    [property: Description("Campos que cambiaron (dedicacion, vigencia, orden).")] IReadOnlyList<string> Campos);

[Description("Comparación de dos versiones del reparto.")]
public sealed record ComparacionDto(
    [property: Description("Versión base.")] int Desde,
    [property: Description("Versión comparada.")] int Hasta,
    [property: Description("Si reparten igual (misma huella).")] bool Iguales,
    [property: Description("Cambios de la base a la comparada.")] IReadOnlyList<CambioVersionDto> Cambios);

[Description("El Markdown del documento de responsables para la vista previa.")]
public sealed record SolicitudVistaPrevia(
    [property: Description("Contenido del documento (Markdown, hasta 100 KB)."), MinLength(1), MaxLength(DocumentoResponsables.TamanoMaximo)] string Markdown);

[Description("Confirmación de la importación: el mismo Markdown y la huella de su vista previa.")]
public sealed record SolicitudImportacion(
    [property: Description("Contenido del documento (Markdown, hasta 100 KB)."), MinLength(1), MaxLength(DocumentoResponsables.TamanoMaximo)] string Markdown,
    [property: Description("Huella de la vista previa: si algo cambió, la importación se rechaza (409)."), MinLength(64), MaxLength(64)] string Huella,
    [property: Description("Estado de traspaso de los secundarios importados: habilitado (por defecto) o en_acompanamiento.")] string? Traspaso);

[Description("Una fila de la vista previa.")]
public sealed record FilaVistaPreviaDto(
    [property: Description("El aplicativo enlazado.")] AplicativoRefDto Aplicativo,
    [property: Description("principal o secundario.")] string Tipo,
    [property: Description("Id de la persona enlazada.")] Guid PersonaId,
    [property: Description("Nombre de la persona en Atlas.")] string Persona,
    [property: Description("Dedicación orientativa.")] int? Dedicacion);

[Description("Algo del documento que no se pudo enlazar.")]
public sealed record PendienteDto(
    [property: Description("aplicativo o persona.")] string Tipo,
    [property: Description("El nombre tal como está en el documento.")] string Nombre,
    [property: Description("no_encontrado o ambiguo.")] string Motivo,
    [property: Description("Qué hacer.")] string Detalle);

[Description("Vista previa de la importación del documento de responsables. Nada se guarda hasta confirmar.")]
public sealed record VistaPreviaDto(
    [property: Description("Versión que dice el documento.")] int? Version,
    [property: Description("Fecha del documento.")] DateOnly? Fecha,
    [property: Description("Quién definió el reparto, según el documento.")] string? DefinidoPor,
    [property: Description("Descripción de la versión, según el documento.")] string? DescripcionVersion,
    [property: Description("Dedicación del secundario que dice el §1.")] int? DedicacionSecundario,
    [property: Description("Próxima revisión que dice el documento.")] DateOnly? ProximaRevision,
    [property: Description("Definiciones de cada rol (§1).")] IReadOnlyList<DefinicionRolDto> Definiciones,
    [property: Description("Responsabilidades enlazadas que quedarían.")] IReadOnlyList<FilaVistaPreviaDto> Filas,
    [property: Description("Lo que no se pudo enlazar: hay que resolverlo antes de confirmar.")] IReadOnlyList<PendienteDto> Pendientes,
    [property: Description("Diferencias entre la tabla 2 y las tablas 3.1 y 3.2.")] IReadOnlyList<string> Inconsistencias,
    [property: Description("Avisos que no impiden confirmar.")] IReadOnlyList<string> Avisos,
    [property: Description("Errores de lectura del documento.")] IReadOnlyList<string> Errores,
    [property: Description("Versión vigente en Atlas; vacía si aún no hay reparto.")] int? VersionActual,
    [property: Description("Si se puede confirmar tal cual.")] bool PuedeConfirmar,
    [property: Description("Huella para confirmar exactamente esto.")] string Huella);

[Description("Cambio de un responsable (o alta de uno nuevo) con su nota: crea una versión.")]
public sealed record SolicitudCambioResponsable(
    [property: Description("Aplicativo del catálogo.")] Guid AplicativoId,
    [property: Description("principal o secundario.")] string Tipo,
    [property: Description("La persona (empleado activo).")] Guid PersonaId,
    [property: Description("Dedicación orientativa (1 a 100); en un secundario nuevo, 20 si no se dice.")] int? Dedicacion,
    [property: Description("Desde cuándo; hoy si no se dice.")] DateOnly? VigenteDesde,
    [property: Description("Hasta cuándo (incluido); vacío sin fin.")] DateOnly? VigenteHasta,
    [property: Description("Por qué cambia (hasta 500)."), MinLength(1), MaxLength(EquipoDbContext.LargoNota)] string Nota);

[Description("Nota de un cambio.")]
public sealed record SolicitudNota(
    [property: Description("Por qué se hace (hasta 500)."), MinLength(1), MaxLength(EquipoDbContext.LargoNota)] string Nota);

[Description("Una actividad del traspaso: despliegue o guardia con el principal.")]
public sealed record SolicitudActividad(
    [property: Description("despliegue o guardia.")] string Tipo,
    [property: Description("Día de la actividad; hoy si no se dice.")] DateOnly? Fecha,
    [property: Description("Nota (hasta 500)."), MaxLength(EquipoDbContext.LargoNota)] string? Nota);

[Description("Una revisión trimestral del reparto.")]
public sealed record RevisionDto(
    [property: Description("Id de la revisión.")] Guid Id,
    [property: Description("Día de la revisión.")] DateOnly Fecha,
    [property: Description("pendiente o hecha.")] string Estado,
    [property: Description("Cuándo salió el recordatorio.")] DateTimeOffset? RecordatorioEnviadoEn,
    [property: Description("Cuándo se hizo.")] DateTimeOffset? HechaEn,
    [property: Description("Quién la cerró.")] string? HechaPor,
    [property: Description("Nota del cierre.")] string? Nota);

[Description("Revisiones del reparto: la próxima y las hechas.")]
public sealed record RevisionesDto(
    [property: Description("La próxima revisión; vacía si no hay una programada.")] RevisionDto? Proxima,
    [property: Description("Revisiones hechas, de la más reciente a la más antigua.")] IReadOnlyList<RevisionDto> Historial,
    [property: Description("Cuántos días antes sale el recordatorio.")] int DiasAnticipacion);

[Description("Fecha de la próxima revisión.")]
public sealed record SolicitudFechaRevision(
    [property: Description("Día de la revisión (hoy o después).")] DateOnly Fecha);

[Description("Cierre de la revisión: queda hecha y se programa la siguiente.")]
public sealed record SolicitudCierreRevision(
    [property: Description("Nota (hasta 500)."), MaxLength(EquipoDbContext.LargoNota)] string? Nota,
    [property: Description("Siguiente revisión; tres meses después si no se dice.")] DateOnly? Siguiente);

[Description("Usuario de GitHub de una persona; vacío lo quita.")]
public sealed record SolicitudUsuarioGitHub(
    [property: Description("Usuario de GitHub, sin @ (hasta 39: letras, números y guiones)."), MaxLength(EquipoDbContext.LargoUsuarioGitHub + 1)] string? Usuario);

[Description("Usuario de GitHub de una persona.")]
public sealed record UsuarioGitHubDto(
    [property: Description("Id de la persona.")] Guid PersonaId,
    [property: Description("Usuario de GitHub; vacío si no tiene.")] string? Usuario);

[Description("Una línea del CODEOWNERS sugerido.")]
public sealed record LineaCodeownersDto(
    [property: Description("* (repo completo) o /carpeta/.")] string Patron,
    [property: Description("El aplicativo de esa carpeta.")] AplicativoRefDto Aplicativo,
    [property: Description("@usuario de principal y secundario.")] IReadOnlyList<string> Propietarios,
    [property: Description("Responsables sin usuario de GitHub en Atlas.")] IReadOnlyList<string> SinUsuario,
    [property: Description("Si los responsables son de un ancestro.")] AplicativoRefDto? HeredadoDe);

[Description("Bloque CODEOWNERS sugerido para un repo.")]
public sealed record BloqueCodeownersDto(
    [property: Description("Repo como dueño/nombre.")] string Repo,
    [property: Description("El bloque, listo para copiar.")] string Texto,
    [property: Description("Sus líneas.")] IReadOnlyList<LineaCodeownersDto> Lineas);

[Description("CODEOWNERS sugerido por repo y carpeta, desde el catálogo y el reparto.")]
public sealed record CodeownersDto(
    [property: Description("Versión vigente del reparto.")] int? Version,
    [property: Description("Si este ambiente puede leer GitHub (Plataforma:GitHub).")] bool GitHubConfigurado,
    [property: Description("token, app o ninguno.")] string ModoGitHub,
    [property: Description("Un bloque por repo.")] IReadOnlyList<BloqueCodeownersDto> Repos,
    [property: Description("Responsables sin usuario de GitHub.")] IReadOnlyList<PersonaEquipoDto> PersonasSinUsuario);

[Description("Diferencia de un patrón entre Atlas y GitHub.")]
public sealed record DiferenciaDto(
    [property: Description("Patrón (carpeta o *).")] string Patron,
    [property: Description("igual, distinto, falta_en_github o solo_en_github.")] string Estado,
    [property: Description("Propietarios según Atlas.")] IReadOnlyList<string> EnAtlas,
    [property: Description("Propietarios en GitHub.")] IReadOnlyList<string> EnGitHub);

[Description("Diferencias entre el CODEOWNERS sugerido y el real de un repo (lectura de la API de GitHub).")]
public sealed record DiferenciasCodeownersDto(
    [property: Description("Repo como dueño/nombre.")] string Repo,
    [property: Description("Si GitHub está configurado en este ambiente.")] bool Configurado,
    [property: Description("Qué falló o qué falta configurar; vacío si se leyó bien.")] string? Mensaje,
    [property: Description("Ruta del CODEOWNERS en el repo; vacía si no tiene.")] string? Archivo,
    [property: Description("Diferencias por patrón.")] IReadOnlyList<DiferenciaDto> Diferencias);

[Description("Ficha de responsables en Markdown para el README del aplicativo.")]
public sealed record FichaResponsablesDto(
    [property: Description("El aplicativo.")] AplicativoRefDto Aplicativo,
    [property: Description("La ficha en Markdown.")] string Markdown);
