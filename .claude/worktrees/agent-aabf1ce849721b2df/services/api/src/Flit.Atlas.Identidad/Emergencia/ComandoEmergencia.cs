using System.Globalization;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Atlas.Identidad.Emergencia;

/// <summary>
/// Comando de consola de la cuenta de emergencia (ATLAS-ADR-0027), dentro de la imagen de atlas-api:
/// <c>kubectl exec -it deploy/atlas-api -- dotnet Flit.Atlas.Api.dll emergencia activar</c>. Usa la configuración del
/// propio contenedor (base, Redis y llaves de Data Protection) y no levanta el servidor HTTP. El enlace solo se escribe
/// en la salida del comando: nunca en el log.
/// </summary>
public static class ComandoEmergencia
{
    public const string Nombre = "emergencia";
    public const string Activar = "activar";

    public const string Uso = "Uso: dotnet Flit.Atlas.Api.dll emergencia activar";

    private static readonly TimeZoneInfo Bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");

    /// <summary>¿Los argumentos del proceso piden el comando y no el servidor?</summary>
    public static bool EsComando(IReadOnlyList<string> args) =>
        args is { Count: > 0 } && string.Equals(args[0], Nombre, StringComparison.OrdinalIgnoreCase);

    /// <returns>Código de salida: 0 si emitió el enlace, 1 si la configuración no sirve, 2 si los argumentos no son válidos.</returns>
    public static async Task<int> EjecutarAsync(
        IServiceProvider servicios, IReadOnlyList<string> args, TextWriter salida, TextWriter errores, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(salida);
        ArgumentNullException.ThrowIfNull(errores);
        if (args.Count != 2 || !string.Equals(args[1], Activar, StringComparison.OrdinalIgnoreCase))
        {
            await errores.WriteLineAsync(Uso);
            return 2;
        }

        await using var alcance = servicios.CreateAsyncScope();
        EnlaceEmergencia enlace;
        try
        {
            enlace = await alcance.ServiceProvider.GetRequiredService<CuentaEmergencia>().ActivarAsync(ct);
        }
        catch (CuentaEmergenciaException ex)
        {
            await errores.WriteLineAsync(ex.Message);
            return 1;
        }

        var vence = TimeZoneInfo.ConvertTime(enlace.VenceEn, Bogota).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        await salida.WriteLineAsync("Cuenta de administrador de emergencia de Flit Atlas");
        await salida.WriteLineAsync($"  Correo: {enlace.Correo}");
        await salida.WriteLineAsync(enlace.CuentaCreada
            ? "  Cuenta creada ahora, con atlas.superadmin sin vencimiento."
            : $"  Factores anteriores retirados ({(enlace.FactoresRetirados.Count == 0 ? "ninguno" : string.Join(", ", enlace.FactoresRetirados))}) y {enlace.SesionesCerradas} sesiones cerradas.");
        await salida.WriteLineAsync();
        await salida.WriteLineAsync($"Enlace de activación (un solo uso, vence el {vence}, hora de Bogotá):");
        await salida.WriteLineAsync(enlace.Url);
        await salida.WriteLineAsync();
        await salida.WriteLineAsync("Ábrelo ya y registra una passkey (o contraseña con aplicación de autenticación). No lo copies en chats,");
        await salida.WriteLineAsync("tickets ni documentos. Volver a correr el comando invalida este enlace y cierra las sesiones de la cuenta.");
        return 0;
    }
}
