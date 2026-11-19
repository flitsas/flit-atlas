using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Plataforma.Modulos;

namespace Flit.Atlas.Modulos.Equipo.Dominio;

/// <summary>Un empleado con el que se puede enlazar un nombre del documento.</summary>
public sealed record PersonaCandidata(Guid Id, string Nombre, string Correo, bool Activa);

/// <summary>Lo que no se pudo enlazar: queda en la vista previa y no se inventa (ATLAS-ADR-0031).</summary>
/// <param name="Tipo"><c>aplicativo</c> o <c>persona</c>.</param>
/// <param name="Motivo"><c>no_encontrado</c> o <c>ambiguo</c>.</param>
public sealed record Pendiente(string Tipo, string Nombre, string Motivo, string Detalle);

/// <summary>
/// La importación del documento ya enlazada: la foto del reparto que quedaría, lo que no se pudo enlazar, las
/// inconsistencias entre la tabla 2 y las tablas 3.x y una huella para confirmar exactamente lo que se vio.
/// </summary>
public sealed record PlanImportacion(
    DocumentoLeido Documento, FotoReparto Foto, IReadOnlyList<Pendiente> Pendientes, IReadOnlyList<string> Inconsistencias,
    IReadOnlyList<string> Avisos)
{
    /// <summary>Sin errores de lectura, sin pendientes y con al menos una responsabilidad.</summary>
    public bool PuedeConfirmar => Documento.Errores.Count == 0 && Pendientes.Count == 0 && Foto.Responsabilidades.Count > 0;

    public string Huella()
    {
        var d = Documento;
        var texto = string.Create(CultureInfo.InvariantCulture,
            $"{d.Version}|{d.Fecha:yyyy-MM-dd}|{d.DefinidoPor}|{d.DedicacionSecundario}|{d.ProximaRevision:yyyy-MM-dd}|{FotoReparto.DefinicionesAJson(d.Definiciones)}|{Foto.Huella()}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
    }

    /// <summary>
    /// Enlaza el documento con el catálogo (productos de la tabla 3.1 con productos; módulos de la 3.2 con módulos, por
    /// nombre sin tildes ni mayúsculas o por slug) y con las personas (por nombre normalizado o por correo).
    /// </summary>
    public static PlanImportacion Armar(
        DocumentoLeido documento, IReadOnlyList<AplicativoLeido> aplicativos, IReadOnlyList<PersonaCandidata> personas, DateOnly hoy)
    {
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentNullException.ThrowIfNull(aplicativos);
        ArgumentNullException.ThrowIfNull(personas);
        var pendientes = new List<Pendiente>();
        var avisos = new List<string>();
        var desde = documento.Fecha ?? hoy;

        // Orden de las personas: el de la tabla 2; las que no estén ahí, al final en el orden en que aparecen.
        var ordenPersona = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fila in documento.PorPersona)
        {
            ordenPersona.TryAdd(Textos.Normalizar(fila.Persona), ordenPersona.Count + 1);
        }
        var listas = documento.PorPersona
            .GroupBy(f => Textos.Normalizar(f.Persona))
            .ToDictionary(g => g.Key, g => (Principal: g.First().PrincipalDe.Select(Textos.Normalizar).ToList(), Secundario: g.First().SecundarioDe.Select(Textos.Normalizar).ToList()), StringComparer.Ordinal);

        var personasEnlazadas = new Dictionary<string, PersonaCandidata?>(StringComparer.Ordinal);
        PersonaCandidata? EnlazarPersona(string nombre)
        {
            var clave = Textos.Normalizar(nombre);
            if (personasEnlazadas.TryGetValue(clave, out var ya))
            {
                return ya;
            }
            var candidatas = nombre.Contains('@', StringComparison.Ordinal)
                ? personas.Where(p => string.Equals(p.Correo, nombre.Trim(), StringComparison.OrdinalIgnoreCase)).ToList()
                : personas.Where(p => Textos.Normalizar(p.Nombre) == clave).ToList();
            if (candidatas.Count > 1 && candidatas.Count(p => p.Activa) == 1)
            {
                candidatas = [.. candidatas.Where(p => p.Activa)];
            }
            PersonaCandidata? elegida = null;
            if (candidatas.Count == 0)
            {
                pendientes.Add(new Pendiente("persona", nombre, "no_encontrado",
                    "No hay un empleado en Atlas con ese nombre o correo. Sincroniza el directorio con Microsoft 365 (o pide que entre una vez) y vuelve a revisar."));
            }
            else if (candidatas.Count > 1)
            {
                pendientes.Add(new Pendiente("persona", nombre, "ambiguo",
                    $"Hay {candidatas.Count} empleados con ese nombre: escribe su correo en el documento para elegir uno."));
            }
            else
            {
                elegida = candidatas[0];
                if (!elegida.Activa)
                {
                    avisos.Add($"«{elegida.Nombre}» está inactiva en Atlas: recibirá la responsabilidad, pero no sus permisos mientras siga inactiva.");
                }
            }
            personasEnlazadas[clave] = elegida;
            return elegida;
        }

        var aplicativosEnlazados = new HashSet<Guid>();
        AplicativoLeido? EnlazarAplicativo(string nombre, string tipo, string tabla)
        {
            var clave = Textos.Normalizar(nombre);
            var slug = clave.Replace(' ', '-');
            var candidatos = aplicativos.Where(a => a.Tipo == tipo && (Textos.Normalizar(a.Nombre) == clave || a.Slug == slug)).ToList();
            if (candidatos.Count == 1)
            {
                if (!aplicativosEnlazados.Add(candidatos[0].Id))
                {
                    pendientes.Add(new Pendiente("aplicativo", nombre, "ambiguo", $"«{nombre}» aparece dos veces en la tabla {tabla}."));
                    return null;
                }
                return candidatos[0];
            }
            pendientes.Add(candidatos.Count == 0
                ? new Pendiente("aplicativo", nombre, "no_encontrado", $"No hay un {(tipo == "producto" ? "producto" : "módulo")} con ese nombre en el catálogo (tabla {tabla}). Créalo en el catálogo y vuelve a revisar.")
                : new Pendiente("aplicativo", nombre, "ambiguo", $"Hay {candidatos.Count} aplicativos con ese nombre en el catálogo: {string.Join(", ", candidatos.Select(c => c.Slug))}."));
            return null;
        }

        var filas = new List<ResponsabilidadFoto>();
        var orden = 0;
        void Agregar(FilaAplicativo fila, string tipoAplicativo, string tabla)
        {
            orden++;
            var aplicativo = EnlazarAplicativo(fila.Aplicativo, tipoAplicativo, tabla);
            foreach (var (tipo, nombre) in new[] { ("principal", fila.Principal), ("secundario", fila.Secundario) })
            {
                if (nombre is null)
                {
                    continue;
                }
                var persona = EnlazarPersona(nombre);
                if (aplicativo is null || persona is null)
                {
                    continue;
                }
                var clavePersona = Textos.Normalizar(nombre);
                if (!ordenPersona.TryGetValue(clavePersona, out var posicion))
                {
                    posicion = ordenPersona.Count + 1;
                    ordenPersona[clavePersona] = posicion;
                }
                var lista = listas.TryGetValue(clavePersona, out var l) ? (tipo == "principal" ? l.Principal : l.Secundario) : [];
                var enLista = lista.IndexOf(Textos.Normalizar(fila.Aplicativo));
                filas.Add(new ResponsabilidadFoto(aplicativo.Id, aplicativo.Slug, aplicativo.Nombre, aplicativo.Tipo, tipo,
                    persona.Id, persona.Nombre, tipo == "secundario" ? documento.DedicacionSecundario : null, desde, null,
                    orden, posicion, enLista >= 0 ? enLista + 1 : 1000 + orden));
            }
        }
        foreach (var fila in documento.Productos)
        {
            Agregar(fila, "producto", "3.1");
        }
        foreach (var fila in documento.Modulos)
        {
            Agregar(fila, "modulo", "3.2");
        }

        return new PlanImportacion(documento, new FotoReparto(filas), pendientes, BuscarInconsistencias(documento), avisos);
    }

    /// <summary>La tabla 2 debe decir lo mismo que las tablas 3.1 y 3.2: si no, la 2 no se podrá reproducir igual.</summary>
    private static List<string> BuscarInconsistencias(DocumentoLeido documento)
    {
        var resultado = new List<string>();
        var filas = documento.Productos.Concat(documento.Modulos).ToList();
        var nombres = filas.SelectMany(f => new[] { f.Principal, f.Secundario }).OfType<string>()
            .GroupBy(Textos.Normalizar).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var fila in documento.PorPersona)
        {
            var persona = Textos.Normalizar(fila.Persona);
            foreach (var (tipo, lista, deTablas) in new[]
                     {
                         ("principal", fila.PrincipalDe, filas.Where(f => Textos.Normalizar(f.Principal) == persona)),
                         ("secundario", fila.SecundarioDe, filas.Where(f => Textos.Normalizar(f.Secundario) == persona)),
                     })
            {
                var enTabla2 = lista.Select(Textos.Normalizar).ToHashSet(StringComparer.Ordinal);
                var enTablas3 = deTablas.Select(f => Textos.Normalizar(f.Aplicativo)).ToHashSet(StringComparer.Ordinal);
                if (!enTabla2.SetEquals(enTablas3))
                {
                    resultado.Add($"{fila.Persona}: la tabla 2 dice {tipo} de «{string.Join(", ", lista)}», pero las tablas 3.1 y 3.2 dicen «{string.Join(", ", deTablas.Select(f => f.Aplicativo))}». Se importa lo de las tablas 3.1 y 3.2.");
                }
            }
            nombres.Remove(persona);
        }
        resultado.AddRange(nombres.Values.Select(n => $"{n} está en las tablas 3.1 o 3.2 pero no en la tabla 2: irá al final del reparto por persona."));
        return resultado;
    }
}
