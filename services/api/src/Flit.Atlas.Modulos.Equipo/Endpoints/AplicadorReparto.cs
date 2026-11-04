using Flit.Atlas.Modulos.Equipo.Datos;
using Flit.Atlas.Modulos.Equipo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Modulos.Equipo.Endpoints;

/// <summary>
/// Lleva el reparto vigente a una foto y agrega su versión (importación, cambio y restauración usan lo mismo). Las
/// responsabilidades se actualizan en su lugar por aplicativo y tipo: si la persona no cambia, conserva su traspaso y su
/// checklist; si cambia, empieza en el estado que diga <c>estadoNuevo</c>.
/// </summary>
internal static class AplicadorReparto
{
    /// <summary>Un secundario nuevo empieza en acompañamiento; un principal, habilitado (§8.2).</summary>
    public static EstadoTraspaso EstadoPorDefecto(TipoResponsabilidad tipo) =>
        tipo == TipoResponsabilidad.Secundario ? EstadoTraspaso.EnAcompanamiento : EstadoTraspaso.Habilitado;

    public sealed record DatosVersion(
        int Numero, OrigenVersion Origen, int? RestauradaDe, DateOnly? FechaDocumento, string? DefinidoPor, Guid Autor, string Nota, string Definiciones);

    public static async Task<VersionReparto> AplicarAsync(
        EquipoDbContext equipo, FotoReparto objetivo, DatosVersion datos, Func<TipoResponsabilidad, EstadoTraspaso> estadoNuevo, CancellationToken ct)
    {
        var actuales = await equipo.Responsabilidades.ToListAsync(ct);
        var porClave = actuales.ToDictionary(r => (r.AplicativoId, r.Tipo));
        var claves = new HashSet<(Guid, TipoResponsabilidad)>();
        foreach (var f in objetivo.Responsabilidades)
        {
            var tipo = Textos.Leer<TipoResponsabilidad>(f.Tipo) ?? throw new InvalidOperationException($"Tipo de responsabilidad desconocido: {f.Tipo}.");
            claves.Add((f.AplicativoId, tipo));
            if (!porClave.TryGetValue((f.AplicativoId, tipo), out var r))
            {
                r = new Responsabilidad
                {
                    AplicativoId = f.AplicativoId,
                    Tipo = tipo,
                    PersonaId = f.PersonaId,
                    VigenteDesde = f.VigenteDesde,
                    EstadoTraspaso = estadoNuevo(tipo),
                };
                equipo.Responsabilidades.Add(r);
            }
            else if (r.PersonaId != f.PersonaId)
            {
                r.PersonaId = f.PersonaId;
                r.EstadoTraspaso = estadoNuevo(tipo);
            }
            r.Dedicacion = f.Dedicacion;
            r.VigenteDesde = f.VigenteDesde;
            r.VigenteHasta = f.VigenteHasta;
            r.OrdenAplicativo = f.OrdenAplicativo;
            r.OrdenPersona = f.OrdenPersona;
            r.OrdenEnPersona = f.OrdenEnPersona;
        }
        equipo.Responsabilidades.RemoveRange(actuales.Where(r => !claves.Contains((r.AplicativoId, r.Tipo))));

        var version = new VersionReparto
        {
            Numero = datos.Numero,
            Origen = datos.Origen,
            RestauradaDe = datos.RestauradaDe,
            FechaDocumento = datos.FechaDocumento,
            DefinidoPor = datos.DefinidoPor,
            AutorPersonaId = datos.Autor,
            Nota = datos.Nota,
            Definiciones = datos.Definiciones,
            Contenido = objetivo.AJson(),
            Huella = objetivo.Huella(),
        };
        equipo.Versiones.Add(version);
        return version;
    }

    /// <summary>
    /// La foto con un responsable cambiado o agregado en (<paramref name="aplicativo"/>, <paramref name="tipo"/>): conserva
    /// el orden del aplicativo y pone el aplicativo al final de la lista de la persona nueva.
    /// </summary>
    public static FotoReparto ConCambio(
        FotoReparto actual, Plataforma.Modulos.AplicativoLeido aplicativo, TipoResponsabilidad tipo, Guid personaId, string persona,
        int? dedicacion, DateOnly desde, DateOnly? hasta)
    {
        var textoTipo = Textos.De(tipo);
        var lista = actual.Responsabilidades.ToList();
        var anterior = lista.FirstOrDefault(r => r.AplicativoId == aplicativo.Id && r.Tipo == textoTipo);
        if (anterior is not null)
        {
            lista.Remove(anterior);
        }
        var ordenAplicativo = anterior?.OrdenAplicativo
            ?? lista.FirstOrDefault(r => r.AplicativoId == aplicativo.Id)?.OrdenAplicativo
            ?? (lista.Count == 0 ? 1 : lista.Max(r => r.OrdenAplicativo) + 1);
        var dePersona = lista.Where(r => r.PersonaId == personaId).ToList();
        var ordenPersona = dePersona.Count > 0 ? dePersona.Min(r => r.OrdenPersona)
            : anterior is not null && anterior.PersonaId == personaId ? anterior.OrdenPersona
            : lista.Count == 0 ? 1 : lista.Max(r => r.OrdenPersona) + 1;
        var ordenEnPersona = anterior is not null && anterior.PersonaId == personaId
            ? anterior.OrdenEnPersona
            : dePersona.Where(r => r.Tipo == textoTipo).Select(r => r.OrdenEnPersona).DefaultIfEmpty(0).Max() + 1;
        lista.Add(new ResponsabilidadFoto(aplicativo.Id, aplicativo.Slug, aplicativo.Nombre, aplicativo.Tipo, textoTipo, personaId, persona,
            dedicacion, desde, hasta, ordenAplicativo, ordenPersona, ordenEnPersona));
        return new FotoReparto(lista);
    }
}
