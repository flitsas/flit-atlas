using System.ComponentModel;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>Un documento legal vigente que un externo debe aceptar.</summary>
[Description("Documento legal vigente (términos de uso o aviso de privacidad, Ley 1581) con su versión y su texto en Markdown.")]
public sealed record DocumentoVigente(
    [property: Description("terminos o privacidad.")] string Documento,
    [property: Description("Versión que se acepta; cambia cuando cambia el texto.")] string Version,
    string Titulo,
    [property: Description("Texto en Markdown, tal como se muestra.")] string Texto,
    [property: Description("SHA-256 (hex) del texto: queda en la evidencia de la aceptación.")] string Hash);

/// <summary>Versión de un documento que la persona dice aceptar.</summary>
[Description("Versión de un documento legal que la persona acepta.")]
public sealed record AceptacionDocumento(
    [property: Description("terminos o privacidad.")] string Documento,
    [property: Description("La versión vigente que se mostró.")] string Version);

/// <summary>
/// Términos de uso y aviso de privacidad (Ley 1581, §6.2 y §12, ATLAS-ADR-0025). Los textos viven en el código
/// (<c>Externos/Legal/*.md</c>, recursos del ensamblado) y la versión aquí: cambiar un texto exige subir su versión, y
/// cada externo acepta la nueva en su siguiente ingreso. La aceptación se guarda como evidencia en
/// <c>plataforma_aceptacion_terminos</c> (versión, hash del texto, fecha, IP y navegador).
/// <para><b>BORRADOR [por verificar]</b>: los textos son de ejemplo; los definitivos los aprueba el líder técnico.</para>
/// </summary>
public sealed class DocumentosLegales(PlataformaDbContext plataforma, TimeProvider reloj)
{
    public const string VersionTerminos = "2026-10-borrador";
    public const string VersionPrivacidad = "2026-10-borrador";

    public static IReadOnlyList<DocumentoVigente> Vigentes { get; } =
    [
        Cargar(DocumentoLegal.Terminos, VersionTerminos, "Términos de uso", "terminos.md"),
        Cargar(DocumentoLegal.Privacidad, VersionPrivacidad, "Aviso de privacidad", "privacidad.md"),
    ];

    public static string Codigo(DocumentoLegal documento) => documento == DocumentoLegal.Terminos ? "terminos" : "privacidad";

    /// <summary>¿La persona aceptó la versión vigente de todos los documentos?</summary>
    public async Task<IReadOnlyList<DocumentoVigente>> PendientesAsync(Guid personaId, CancellationToken ct)
    {
        var aceptadas = await plataforma.AceptacionesTerminos.AsNoTracking()
            .Where(a => a.PersonaId == personaId)
            .Select(a => new { a.Documento, a.Version })
            .ToListAsync(ct);
        return [.. Vigentes.Where(v => !aceptadas.Any(a => Codigo(a.Documento) == v.Documento && a.Version == v.Version))];
    }

    /// <summary>¿Lo aceptado es exactamente la versión vigente de cada documento?</summary>
    public static bool AceptaTodo(IReadOnlyCollection<AceptacionDocumento>? aceptados) =>
        aceptados is not null && Vigentes.All(v => aceptados.Any(a =>
            string.Equals(a.Documento, v.Documento, StringComparison.Ordinal) && string.Equals(a.Version, v.Version, StringComparison.Ordinal)));

    /// <summary>Guarda la evidencia de las versiones vigentes que la persona aún no tenía (sin guardar cambios).</summary>
    public async Task RegistrarAsync(Guid personaId, Guid? invitacionId, HttpContext? http, CancellationToken ct)
    {
        var pendientes = await PendientesAsync(personaId, ct);
        var ahora = reloj.GetUtcNow();
        var agente = http?.Request.Headers.UserAgent.ToString();
        foreach (var documento in pendientes)
        {
            plataforma.AceptacionesTerminos.Add(new AceptacionTerminos
            {
                PersonaId = personaId,
                Documento = documento.Documento == "terminos" ? DocumentoLegal.Terminos : DocumentoLegal.Privacidad,
                Version = documento.Version,
                HashContenido = documento.Hash,
                InvitacionId = invitacionId,
                AceptadoEn = ahora,
                Ip = RegistroSeguridad.IpCliente(http),
                AgenteUsuario = string.IsNullOrEmpty(agente) ? null : agente[..Math.Min(agente.Length, 500)],
            });
        }
    }

    private static DocumentoVigente Cargar(DocumentoLegal documento, string version, string titulo, string archivo)
    {
        using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Flit.Atlas.Identidad.Externos.Legal.{archivo}")
            ?? throw new InvalidOperationException($"Falta el recurso Legal/{archivo}.");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        var texto = lector.ReadToEnd().ReplaceLineEndings("\n");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)));
        return new DocumentoVigente(Codigo(documento), version, titulo, texto, hash);
    }
}
