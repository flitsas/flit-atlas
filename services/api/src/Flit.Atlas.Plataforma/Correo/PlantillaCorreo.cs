using System.Net;
using System.Text;

namespace Flit.Atlas.Plataforma.Correo;

/// <summary>
/// Plantilla de los correos de Atlas con la marca FLIT (azul <c>#162744</c>, acción <c>#3a56c5</c>, Poppins con
/// respaldo del sistema), en español. Los correos sí llevan estilos en línea: los clientes de correo ignoran las hojas de
/// estilo (la CSP estricta es de la PWA, no del correo). Todo texto se codifica en HTML.
/// </summary>
public static class PlantillaCorreo
{
    private const string Azul = "#162744";
    private const string Accion = "#3a56c5";
    private const string TextoSuave = "#59677d";

    /// <param name="titulo">Encabezado del correo.</param>
    /// <param name="parrafos">Párrafos antes del botón.</param>
    /// <param name="boton">Texto del botón; <c>null</c> para un aviso sin botón ni enlace (avisos de seguridad).</param>
    /// <param name="enlace">URL del botón (https del ambiente); <c>null</c> sin botón.</param>
    /// <param name="notas">Párrafos pequeños después del botón (vencimiento, «si no fuiste tú»…).</param>
    public static (string Html, string Texto) Armar(string titulo, IReadOnlyList<string> parrafos, string? boton, string? enlace, IReadOnlyList<string> notas)
    {
        ArgumentNullException.ThrowIfNull(parrafos);
        ArgumentNullException.ThrowIfNull(notas);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"es-CO\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(H(titulo)).Append("</title></head>")
            .Append("<body style=\"margin:0;padding:0;background:#eef5ff;font-family:Poppins,'Segoe UI',Roboto,Arial,sans-serif;color:#0f172a\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:#eef5ff;padding:24px 12px\"><tr><td align=\"center\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;background:#ffffff;border-radius:16px;overflow:hidden;border:1px solid #dfe5ed\">")
            .Append("<tr><td style=\"background:").Append(Azul).Append(";padding:20px 24px;color:#ffffff;font-size:18px;font-weight:600\">Flit Atlas</td></tr>")
            .Append("<tr><td style=\"padding:28px 24px 8px\"><h1 style=\"margin:0 0 16px;font-size:20px;line-height:1.3;color:").Append(Azul).Append("\">")
            .Append(H(titulo)).Append("</h1>");
        foreach (var parrafo in parrafos)
        {
            html.Append("<p style=\"margin:0 0 14px;font-size:15px;line-height:1.55\">").Append(H(parrafo)).Append("</p>");
        }
        if (boton is not null && enlace is not null)
        {
            html.Append("<p style=\"margin:24px 0\"><a href=\"").Append(H(enlace)).Append("\" style=\"display:inline-block;background:").Append(Accion)
                .Append(";color:#ffffff;text-decoration:none;font-weight:600;font-size:15px;padding:14px 22px;border-radius:12px\">")
                .Append(H(boton)).Append("</a></p>")
                .Append("<p style=\"margin:0 0 14px;font-size:13px;line-height:1.5;color:").Append(TextoSuave)
                .Append("\">Si el botón no funciona, copia este enlace en tu navegador:<br><span style=\"word-break:break-all\">")
                .Append(H(enlace)).Append("</span></p>");
        }
        foreach (var nota in notas.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            html.Append("<p style=\"margin:0 0 10px;font-size:13px;line-height:1.5;color:").Append(TextoSuave).Append("\">").Append(H(nota)).Append("</p>");
        }
        html.Append("</td></tr><tr><td style=\"padding:16px 24px 24px;font-size:12px;color:").Append(TextoSuave)
            .Append("\">FLIT S.A.S. · Este correo lo envió Flit Atlas automáticamente; no respondas a este mensaje.</td></tr>")
            .Append("</table></td></tr></table></body></html>");

        var texto = new StringBuilder();
        texto.Append(titulo).Append("\n\n");
        foreach (var parrafo in parrafos)
        {
            texto.Append(parrafo).Append("\n\n");
        }
        if (boton is not null && enlace is not null)
        {
            texto.Append(boton).Append(": ").Append(enlace).Append("\n\n");
        }
        foreach (var nota in notas.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            texto.Append(nota).Append('\n');
        }
        texto.Append("\nFLIT S.A.S. · Este correo lo envió Flit Atlas automáticamente; no respondas a este mensaje.\n");
        return (html.ToString(), texto.ToString());
    }

    private static string H(string valor) => WebUtility.HtmlEncode(valor);
}
