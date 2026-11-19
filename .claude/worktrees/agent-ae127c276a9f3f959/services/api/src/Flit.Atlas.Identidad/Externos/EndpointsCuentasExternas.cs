using System.ComponentModel;
using System.Security.Claims;
using Flit.Atlas.Identidad.Datos;
using Flit.Atlas.Identidad.Seguridad;
using Flit.Atlas.Identidad.Sesiones;
using Flit.Atlas.Plataforma.Datos;
using Flit.Atlas.Plataforma.Entidades;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Flit.Atlas.Identidad.Externos;

/// <summary>
/// Endpoints públicos de las cuentas externas (HU 1.5, ATLAS-ADR-0025), bajo <c>/api/v1/identidad</c>: aceptar una
/// invitación, entrar con passkey o con contraseña y TOTP, aceptar documentos nuevos y recuperar la cuenta. No exigen
/// sesión (son el camino para tenerla) y tienen el límite de tasa más estricto de los externos; los que abren sesión
/// apartan la cookie anterior (<see cref="RenovacionAlIngresar"/>). Los mensajes de error no revelan si un correo
/// existe.
/// </summary>
public static class EndpointsCuentasExternas
{
    [Description("Token del enlace de la invitación (el fragmento de la URL del correo).")]
    public sealed record SolicitudToken(string Token);

    [Description("Token de la invitación y el nombre que se mostrará en la passkey.")]
    public sealed record SolicitudOpcionesInvitacion(string Token, string Nombre);

    [Description("Correo y contraseña de una cuenta externa (primer paso; el segundo es el TOTP).")]
    public sealed record SolicitudIngresoLocal(string Correo, string Contrasena);

    [Description("Siguiente paso del ingreso.")]
    public sealed record PasoIngreso([property: Description("totp.")] string Paso, [property: Description("Id del paso; vence en 5 minutos.")] string Estado);

    [Description("Segundo factor del ingreso con contraseña: el código TOTP o un código de recuperación.")]
    public sealed record SolicitudSegundoFactor(
        string Estado,
        [property: Description("Código de 6 dígitos de la aplicación de autenticación.")] string? Codigo,
        [property: Description("Código de recuperación de un solo uso, en lugar del TOTP.")] string? CodigoRecuperacion);

    [Description("Respuesta del autenticador para entrar con passkey.")]
    public sealed record SolicitudPasskey(
        string Estado,
        [property: Description("PublicKeyCredential.toJSON() de navigator.credentials.get(), como texto JSON.")] string Credencial);

    [Description("Aceptación de las versiones nuevas de los documentos legales para terminar el ingreso.")]
    public sealed record SolicitudTerminos(string Estado, IReadOnlyList<AceptacionDocumento> Documentos);

    [Description("Antes de entrar hay que aceptar versiones nuevas de los documentos legales.")]
    public sealed record TerminosPendientes(
        [property: Description("terminos_pendientes.")] string Error,
        [property: Description("Id del paso para POST /api/v1/identidad/entrar/terminos.")] string Estado,
        IReadOnlyList<DocumentoVigente> Documentos);

    [Description("Correo de la cuenta externa que se quiere recuperar.")]
    public sealed record SolicitudCorreo(string Correo);

    [Description("Enlace de recuperación (el fragmento de la URL del correo).")]
    public sealed record SolicitudEnlace(string Enlace);

    [Description("Cuenta que se va a recuperar.")]
    public sealed record CuentaARecuperar(string Correo, string Nombre);

    [Description("Enlace de recuperación y el factor nuevo.")]
    public sealed record SolicitudCompletarRecuperacion(string Enlace, SolicitudFactor Factor);

    [Description("Resultado del restablecimiento forzado.")]
    public sealed record ResultadoRestablecimiento([property: Description("false si el correo con el enlace no salió.")] bool CorreoEnviado);

    private sealed record PasoContrasena(Guid UsuarioId, string Sello);

    private sealed record PasoIngresoPasskey(string Estado);

    private sealed record PasoTerminos(Guid UsuarioId, string Metodo, string Sello);

    private const string TipoPasoContrasena = "ingreso-contrasena";
    private const string TipoPasoPasskey = "ingreso-passkey";
    private const string TipoPasoTerminos = "ingreso-terminos";

    /// <summary>Rutas que abren una sesión o la preparan: apartan la cookie anterior y no leen la sesión.</summary>
    internal static readonly string[] RutasDeIngreso =
    [
        "/api/v1/identidad/entrar/local",
        "/api/v1/identidad/entrar/passkey",
        "/api/v1/identidad/entrar/terminos",
        "/api/v1/identidad/invitaciones",
        "/api/v1/identidad/recuperacion",
    ];

    internal static bool EsRutaDeIngreso(PathString ruta) =>
        RutasDeIngreso.Any(r => ruta.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase));

    private static string? _hashFicticio;

    internal static void MapCuentasExternas(this RouteGroupBuilder identidad)
    {
        identidad.MapGet("/terminos", () => DocumentosLegales.Vigentes)
            .WithName("ListarDocumentosLegales")
            .WithSummary("Términos de uso y aviso de privacidad vigentes")
            .WithDescription("Versión y texto (Markdown) de los documentos que acepta un externo al crear su cuenta (Ley 1581). No requiere sesión.");

        var invitaciones = identidad.MapGroup("/invitaciones");
        invitaciones.MapPost("/consultar", ConsultarInvitacionAsync)
            .WithName("ConsultarInvitacion")
            .WithSummary("Ver una invitación antes de aceptarla")
            .WithDescription("Organización, rol, vencimientos y documentos de la invitación del enlace. 410 si el enlace se usó, venció, se revocó o no existe (la misma respuesta en todos los casos). No requiere sesión.")
            .Produces<InvitacionPublica>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        invitaciones.MapPost("/passkey/opciones", OpcionesPasskeyInvitacionAsync)
            .WithName("OpcionesPasskeyInvitacion")
            .WithSummary("Opciones para crear la passkey de la cuenta")
            .WithDescription("Desafío de WebAuthn (navigator.credentials.create) para la cuenta que se crea con la invitación. Vence en 5 minutos y sirve una sola vez. No requiere sesión.")
            .Produces<OpcionesPasskey>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        invitaciones.MapPost("/totp/clave", ClaveTotpInvitacionAsync)
            .WithName("ClaveTotpInvitacion")
            .WithSummary("Clave TOTP para la cuenta con contraseña")
            .WithDescription("Clave nueva y su QR (SVG) para la aplicación de autenticación. Se confirma con el primer código al aceptar. No requiere sesión.")
            .Produces<ClaveTotp>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        invitaciones.MapPost("/aceptar", AceptarInvitacionAsync)
            .WithName("AceptarInvitacion")
            .WithSummary("Aceptar la invitación y crear la cuenta")
            .WithDescription(
                "Crea la cuenta local con passkey (preferida) o contraseña (mínimo 12, revisada contra filtraciones) y TOTP, guarda la " +
                "aceptación de los términos y del aviso de privacidad, asigna el rol de la invitación y abre la sesión. Devuelve los diez " +
                "códigos de recuperación (se muestran una sola vez). El enlace deja de servir. No requiere sesión.")
            .Produces<CuentaLista>()
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict)
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);

        var entrar = identidad.MapGroup("/entrar");
        entrar.MapPost("/local", EntrarConContrasenaAsync)
            .WithName("EntrarConContrasena")
            .WithSummary("Ingreso de externos con contraseña (paso 1)")
            .WithDescription(
                "Verifica correo y contraseña con bloqueo progresivo y responde el paso del TOTP; nunca abre sesión solo con la " +
                "contraseña. 401 genérico si no coinciden o la cuenta está bloqueada; 403 mfa_requerido si la cuenta no tiene segundo factor. No requiere sesión.")
            .Produces<PasoIngreso>()
            .Produces<ErrorExternos>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorExternos>(StatusCodes.Status403Forbidden);
        entrar.MapPost("/local/segundo-factor", SegundoFactorAsync)
            .WithName("EntrarConSegundoFactor")
            .WithSummary("Ingreso de externos con contraseña (paso 2: TOTP)")
            .WithDescription(
                "Código TOTP (o un código de recuperación) del paso anterior. Los fallos suman al bloqueo progresivo y un código no " +
                "se acepta dos veces. 204 abre la sesión; 409 terminos_pendientes si hay versiones nuevas de los documentos. No requiere sesión.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<TerminosPendientes>(StatusCodes.Status409Conflict)
            .Produces<ErrorExternos>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorExternos>(StatusCodes.Status403Forbidden);
        entrar.MapPost("/passkey/opciones", OpcionesPasskeyIngresoAsync)
            .WithName("OpcionesPasskeyIngreso")
            .WithSummary("Opciones para entrar con passkey")
            .WithDescription("Desafío de WebAuthn (navigator.credentials.get) sin usuario: la passkey se descubre en el dispositivo. No requiere sesión.")
            .Produces<OpcionesPasskey>();
        entrar.MapPost("/passkey", EntrarConPasskeyAsync)
            .WithName("EntrarConPasskey")
            .WithSummary("Ingreso de externos con passkey")
            .WithDescription("Verifica la respuesta del autenticador (verificación de usuario obligatoria: cuenta como MFA). 204 abre la sesión; 409 terminos_pendientes. No requiere sesión.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<TerminosPendientes>(StatusCodes.Status409Conflict)
            .Produces<ErrorExternos>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorExternos>(StatusCodes.Status403Forbidden);
        entrar.MapPost("/terminos", AceptarTerminosAsync)
            .WithName("AceptarTerminosAlEntrar")
            .WithSummary("Aceptar documentos nuevos para terminar el ingreso")
            .WithDescription("Guarda la aceptación de las versiones vigentes (evidencia, Ley 1581) y abre la sesión. No requiere sesión.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status401Unauthorized);

        var recuperacion = identidad.MapGroup("/recuperacion");
        recuperacion.MapPost("/", SolicitarRecuperacionAsync)
            .WithName("SolicitarRecuperacion")
            .WithSummary("Pedir el enlace para recuperar la cuenta")
            .WithDescription("Si el correo es de una cuenta externa, envía un enlace de un solo uso que vence en 30 minutos. Responde 202 siempre (no revela si la cuenta existe). No requiere sesión.")
            .Produces(StatusCodes.Status202Accepted);
        recuperacion.MapPost("/consultar", ConsultarRecuperacionAsync)
            .WithName("ConsultarRecuperacion")
            .WithSummary("Ver la cuenta del enlace de recuperación")
            .WithDescription("410 si el enlace se usó, venció o no es válido. No requiere sesión.")
            .Produces<CuentaARecuperar>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        recuperacion.MapPost("/passkey/opciones", OpcionesPasskeyRecuperacionAsync)
            .WithName("OpcionesPasskeyRecuperacion")
            .WithSummary("Opciones para registrar una passkey nueva")
            .WithDescription("Desafío de WebAuthn para el factor nuevo de la cuenta que se recupera. No requiere sesión.")
            .Produces<OpcionesPasskey>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        recuperacion.MapPost("/totp/clave", ClaveTotpRecuperacionAsync)
            .WithName("ClaveTotpRecuperacion")
            .WithSummary("Clave TOTP nueva para la cuenta que se recupera")
            .WithDescription("Clave nueva y su QR para la aplicación de autenticación. No requiere sesión.")
            .Produces<ClaveTotp>()
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);
        recuperacion.MapPost("/completar", CompletarRecuperacionAsync)
            .WithName("CompletarRecuperacion")
            .WithSummary("Registrar el factor nuevo y recuperar la cuenta")
            .WithDescription(
                "Quita todos los factores anteriores, registra el nuevo, genera códigos de recuperación nuevos, quita el bloqueo, " +
                "cierra todas las sesiones y, si la persona puede entrar, abre una nueva. El enlace deja de servir. No requiere sesión.")
            .Produces<CuentaLista>()
            .Produces<ErrorExternos>(StatusCodes.Status400BadRequest)
            .Produces<ErrorExternos>(StatusCodes.Status410Gone);

        // Administración: permiso atlas.identidad.administrar y reautenticación, como suspender (ATLAS-ADR-0022).
        identidad.MapGroup("/admin/personas/{personaId:guid}")
            .RequireAuthorization(PoliticasIdentidad.Administrar)
            .RequiereReautenticacion()
            .MapPost("/restablecer", async ([Description("Persona externa (plataforma_persona).")] Guid personaId, ClaimsPrincipal usuario,
                CuentasExternas cuentas, HttpContext http, CancellationToken ct) =>
            {
                var actor = Guid.TryParse(usuario.FindFirstValue(ClaimsAtlas.PersonaId), out var id) ? id : (Guid?)null;
                var resultado = await cuentas.RestablecerAsync(personaId, actor, http, ct);
                return resultado.Error is { } error
                    ? Results.Json(error, statusCode: resultado.Estado)
                    : Results.Ok(new ResultadoRestablecimiento(resultado.Valor));
            })
            .WithName("RestablecerCuentaExterna")
            .WithSummary("Forzar el restablecimiento de una cuenta externa")
            .WithDescription(
                "Administración: requiere atlas.identidad.administrar. Quita todos los factores de la cuenta (passkeys, contraseña, " +
                "TOTP y códigos), cierra sus sesiones y le envía un enlace de recuperación. 409 si la persona es un empleado o aún no " +
                "tiene cuenta. Exige reautenticación.")
            .Produces<ResultadoRestablecimiento>()
            .Produces<ErrorExternos>(StatusCodes.Status404NotFound)
            .Produces<ErrorExternos>(StatusCodes.Status409Conflict);
    }

    private static IResult Error(int estado, string codigo, string mensaje) => Results.Json(new ErrorExternos(codigo, mensaje), statusCode: estado);

    private static IResult Respuesta<T>(ResultadoCuenta<T> resultado) =>
        resultado.Error is { } error ? Results.Json(error, statusCode: resultado.Estado) : Results.Ok(resultado.Valor);

    private static IResult EnlaceNoValido() =>
        Error(StatusCodes.Status410Gone, CuentasExternas.EnlaceNoValido, "Este enlace ya se usó, venció o no es válido. Pide a quien te invitó que te lo reenvíe.");

    // --- Invitación ---

    private static async Task<IResult> ConsultarInvitacionAsync([Description("Token del enlace de la invitación.")] SolicitudToken solicitud, CuentasExternas cuentas, CancellationToken ct)
    {
        var invitacion = await cuentas.InvitacionValidaAsync(solicitud.Token, ct);
        if (invitacion is null)
        {
            await cuentas.RechazarEnlaceAsync("invitacion", solicitud.Token, ct);
            return EnlaceNoValido();
        }
        return Results.Ok(await cuentas.PublicaAsync(invitacion, ct));
    }

    private static async Task<IResult> OpcionesPasskeyInvitacionAsync(
        [Description("Token de la invitación y nombre para la passkey.")] SolicitudOpcionesInvitacion solicitud, CuentasExternas cuentas, FactoresExternos factores, HttpContext http, CancellationToken ct)
    {
        var invitacion = await cuentas.InvitacionValidaAsync(solicitud.Token, ct);
        if (invitacion is null)
        {
            return EnlaceNoValido();
        }
        var nombre = string.IsNullOrWhiteSpace(solicitud.Nombre) ? invitacion.Correo : solicitud.Nombre.Trim();
        return Results.Ok(await factores.OpcionesRegistroAsync(
            Guid.CreateVersion7(), invitacion.Correo, nombre[..Math.Min(nombre.Length, 200)], CuentasExternas.AmbitoInvitacion(invitacion.Id), http));
    }

    private static async Task<IResult> ClaveTotpInvitacionAsync([Description("Token del enlace de la invitación.")] SolicitudToken solicitud, CuentasExternas cuentas, FactoresExternos factores, CancellationToken ct)
    {
        var invitacion = await cuentas.InvitacionValidaAsync(solicitud.Token, ct);
        return invitacion is null
            ? EnlaceNoValido()
            : Results.Ok(await factores.NuevaClaveTotpAsync(invitacion.Correo, CuentasExternas.AmbitoInvitacion(invitacion.Id)));
    }

    private static async Task<IResult> AceptarInvitacionAsync(
        [Description("Nombre, documentos aceptados y factor de la cuenta.")] SolicitudAceptacion solicitud, CuentasExternas cuentas, SesionExterna sesion, HttpContext http, CancellationToken ct) =>
        Respuesta(await cuentas.AceptarAsync(solicitud, http, sesion, ct));

    // --- Ingreso ---

    private static IResult CredencialesInvalidas() => Error(StatusCodes.Status401Unauthorized, "credenciales_invalidas",
        "Correo o contraseña incorrectos, o la cuenta está bloqueada por intentos fallidos. Si no recuerdas tus datos, recupera tu cuenta.");

    private static IResult PasoVencido() => Error(StatusCodes.Status401Unauthorized, "paso_vencido", "El ingreso tardó demasiado. Empieza de nuevo.");

    private static IResult NoPermitido() => Error(StatusCodes.Status403Forbidden, "ingreso_no_permitido",
        "Tu acceso a Atlas no está vigente. Escribe a quien te invitó para renovarlo.");

    private static async Task<IResult> EntrarConContrasenaAsync(
        [Description("Correo y contraseña.")] SolicitudIngresoLocal solicitud, UserManager<UsuarioAtlas> usuarios, VerificadorContrasena verificador, PlataformaDbContext plataforma,
        SesionExterna sesion, PasosTemporales pasos, RegistroSeguridad registro, CancellationToken ct)
    {
        var correo = (solicitud.Correo ?? string.Empty).Trim().ToLowerInvariant();
        var usuario = correo.Length is > 0 and <= 320 ? await usuarios.FindByEmailAsync(correo) : null;
        var persona = usuario is null ? null : await plataforma.Personas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == usuario.PersonaId, ct);
        if (usuario is null || persona is null || persona.Tipo != TipoPersona.Externo)
        {
            // Mismo trabajo que una verificación real: el tiempo no dice si el correo existe.
            var ficticio = new UsuarioAtlas { PersonaId = Guid.Empty };
            _hashFicticio ??= usuarios.PasswordHasher.HashPassword(ficticio, Guid.NewGuid().ToString());
            usuarios.PasswordHasher.VerifyHashedPassword(ficticio, _hashFicticio, solicitud.Contrasena ?? string.Empty);
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioFallido, new DatosEventoSeguridad
            {
                Detalle = new { metodo = MetodosExternos.Contrasena, motivo = "cuenta_desconocida" },
            }, ct);
            return CredencialesInvalidas();
        }
        if (await verificador.VerificarAsync(usuario, solicitud.Contrasena ?? string.Empty, ct) != ResultadoVerificacion.Valida)
        {
            return CredencialesInvalidas();
        }
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = persona.Id, OrganizacionId = persona.OrganizacionId };
        if (await sesion.MotivoNoPermitidoAsync(persona.Id, ct) is { } motivo)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, datos with { Detalle = new { metodo = MetodosExternos.Contrasena, motivo } }, ct);
            return NoPermitido();
        }
        if (!(await usuarios.GetTwoFactorEnabledAsync(usuario) && await usuarios.GetAuthenticatorKeyAsync(usuario) is not null))
        {
            // CA de la HU 1.5: una cuenta externa sin MFA no puede entrar.
            await registro.RegistrarAsync(TiposEventoSeguridad.MfaRequerido, datos with { Detalle = new { metodo = MetodosExternos.Contrasena } }, ct);
            return Error(StatusCodes.Status403Forbidden, "mfa_requerido",
                "Tu cuenta no tiene un segundo factor. Recupérala con el enlace que te enviamos por correo para registrar uno.");
        }
        var estado = await pasos.GuardarAsync(TipoPasoContrasena, new PasoContrasena(usuario.Id, usuario.SecurityStamp ?? string.Empty));
        return Results.Ok(new PasoIngreso("totp", estado));
    }

    private static async Task<IResult> SegundoFactorAsync(
        [Description("Paso del ingreso y código TOTP o de recuperación.")] SolicitudSegundoFactor solicitud, UserManager<UsuarioAtlas> usuarios, VerificadorContrasena verificador, PasosTemporales pasos,
        IConnectionMultiplexer redis, SesionExterna sesion, DocumentosLegales legales, RegistroSeguridad registro,
        HttpContext http, CancellationToken ct)
    {
        var paso = await pasos.LeerAsync<PasoContrasena>(TipoPasoContrasena, solicitud.Estado);
        var usuario = paso is null ? null : await usuarios.FindByIdAsync(paso.UsuarioId.ToString());
        if (paso is null || usuario is null || usuario.SecurityStamp != paso.Sello)
        {
            return PasoVencido();
        }
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = usuario.PersonaId };
        ResultadoVerificacion resultado;
        string metodo;
        if (!string.IsNullOrWhiteSpace(solicitud.CodigoRecuperacion))
        {
            metodo = "codigo_recuperacion";
            resultado = await verificador.CanjearCodigoRecuperacionAsync(usuario, solicitud.CodigoRecuperacion, ct);
        }
        else
        {
            metodo = "totp";
            var codigo = (solicitud.Codigo ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
            // Un código TOTP no sirve dos veces (ASVS V2.8.4): se recuerda por usuario mientras pueda seguir vigente.
            var nuevo = codigo.Length is > 0 and <= 10 && await redis.GetDatabase().StringSetAsync(
                $"{PasosTemporales.Prefijo}totp-usado:{usuario.Id:N}:{codigo}", 1, TimeSpan.FromMinutes(3), When.NotExists);
            resultado = nuevo ? await verificador.VerificarTotpAsync(usuario, codigo, ct) : ResultadoVerificacion.Invalida;
            if (!nuevo)
            {
                await registro.RegistrarAsync(TiposEventoSeguridad.MfaFallido, datos with { Detalle = new { metodo, motivo = "codigo_reutilizado" } }, ct);
            }
        }
        if (resultado == ResultadoVerificacion.Bloqueada)
        {
            await pasos.BorrarAsync(TipoPasoContrasena, solicitud.Estado);
            return Error(StatusCodes.Status401Unauthorized, "cuenta_bloqueada", "La cuenta quedó bloqueada un tiempo por intentos fallidos.");
        }
        if (resultado != ResultadoVerificacion.Valida)
        {
            return Error(StatusCodes.Status401Unauthorized, "codigo_invalido", "El código no es válido. Escribe el código actual de tu aplicación.");
        }
        await pasos.BorrarAsync(TipoPasoContrasena, solicitud.Estado);
        await registro.RegistrarAsync(TiposEventoSeguridad.MfaVerificado, datos with { Detalle = new { metodo } }, ct);
        if (metodo == "codigo_recuperacion")
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.CodigoRecuperacionUsado, datos with
            {
                Detalle = new { restantes = await usuarios.CountRecoveryCodesAsync(usuario) },
            }, ct);
        }
        return await CompletarIngresoAsync(http, usuario, MetodosExternos.Contrasena, sesion, legales, pasos, registro, ct);
    }

    private static async Task<IResult> OpcionesPasskeyIngresoAsync(IPasskeyHandler<UsuarioAtlas> passkeys, PasosTemporales pasos, HttpContext http)
    {
        var opciones = await passkeys.MakeRequestOptionsAsync(null, http);
        var estado = await pasos.GuardarAsync(TipoPasoPasskey, new PasoIngresoPasskey(opciones.AssertionState!));
        return Results.Ok(new OpcionesPasskey(estado, opciones.RequestOptionsJson));
    }

    private static async Task<IResult> EntrarConPasskeyAsync(
        [Description("Paso del ingreso y respuesta del autenticador.")] SolicitudPasskey solicitud, IPasskeyHandler<UsuarioAtlas> passkeys, UserManager<UsuarioAtlas> usuarios, PasosTemporales pasos,
        SesionExterna sesion, DocumentosLegales legales, RegistroSeguridad registro, HttpContext http, CancellationToken ct)
    {
        var paso = await pasos.TomarAsync<PasoIngresoPasskey>(TipoPasoPasskey, solicitud.Estado);
        if (paso is null || string.IsNullOrEmpty(solicitud.Credencial))
        {
            return PasoVencido();
        }
        var resultado = await passkeys.PerformAssertionAsync(new PasskeyAssertionContext
        {
            CredentialJson = solicitud.Credencial,
            AssertionState = paso.Estado,
            HttpContext = http,
        });
        if (!resultado.Succeeded)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioFallido, new DatosEventoSeguridad
            {
                Detalle = new { metodo = MetodosExternos.Passkey, motivo = "passkey_invalida" },
            }, ct);
            return Error(StatusCodes.Status401Unauthorized, "passkey_invalida", "No se pudo verificar la passkey. Vuelve a intentarlo.");
        }
        var usuario = resultado.User;
        var datos = new DatosEventoSeguridad { UsuarioId = usuario.Id, PersonaId = usuario.PersonaId };
        if (await usuarios.IsLockedOutAsync(usuario))
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, datos with { Detalle = new { metodo = MetodosExternos.Passkey, motivo = "cuenta_bloqueada" } }, ct);
            return NoPermitido();
        }
        // Contador de firmas y estado de respaldo actualizados (detecta autenticadores clonados).
        await usuarios.AddOrUpdatePasskeyAsync(usuario, resultado.Passkey);
        await usuarios.ResetAccessFailedCountAsync(usuario);
        await registro.RegistrarAsync(TiposEventoSeguridad.MfaVerificado, datos with { Detalle = new { metodo = MetodosExternos.Passkey } }, ct);
        return await CompletarIngresoAsync(http, usuario, MetodosExternos.Passkey, sesion, legales, pasos, registro, ct);
    }

    private static async Task<IResult> AceptarTerminosAsync(
        [Description("Paso del ingreso y versiones aceptadas.")] SolicitudTerminos solicitud, UserManager<UsuarioAtlas> usuarios, PasosTemporales pasos, SesionExterna sesion, DocumentosLegales legales,
        PlataformaDbContext plataforma, RegistroSeguridad registro, HttpContext http, CancellationToken ct)
    {
        var paso = await pasos.LeerAsync<PasoTerminos>(TipoPasoTerminos, solicitud.Estado);
        var usuario = paso is null ? null : await usuarios.FindByIdAsync(paso.UsuarioId.ToString());
        if (paso is null || usuario is null || usuario.SecurityStamp != paso.Sello)
        {
            return PasoVencido();
        }
        if (!DocumentosLegales.AceptaTodo(solicitud.Documentos))
        {
            return Error(StatusCodes.Status400BadRequest, "terminos_no_aceptados", "Para entrar debes aceptar la versión vigente de los documentos.");
        }
        await pasos.BorrarAsync(TipoPasoTerminos, solicitud.Estado);
        await legales.RegistrarAsync(usuario.PersonaId, null, http, ct);
        await plataforma.SaveChangesAsync(ct);
        await registro.RegistrarAsync(TiposEventoSeguridad.TerminosAceptados, new DatosEventoSeguridad
        {
            UsuarioId = usuario.Id,
            PersonaId = usuario.PersonaId,
            Detalle = new { documentos = DocumentosLegales.Vigentes.Select(d => new { d.Documento, d.Version, d.Hash }) },
        }, ct);
        return await CompletarIngresoAsync(http, usuario, paso.Metodo, sesion, legales, pasos, registro, ct);
    }

    /// <summary>Último control (acceso vigente y documentos al día) y la sesión.</summary>
    private static async Task<IResult> CompletarIngresoAsync(
        HttpContext http, UsuarioAtlas usuario, string metodo, SesionExterna sesion, DocumentosLegales legales, PasosTemporales pasos,
        RegistroSeguridad registro, CancellationToken ct)
    {
        if (await sesion.MotivoNoPermitidoAsync(usuario.PersonaId, ct) is { } motivo)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.InicioRechazado, new DatosEventoSeguridad
            {
                UsuarioId = usuario.Id,
                PersonaId = usuario.PersonaId,
                Detalle = new { metodo, motivo },
            }, ct);
            return NoPermitido();
        }
        var pendientes = await legales.PendientesAsync(usuario.PersonaId, ct);
        if (pendientes.Count > 0)
        {
            var estado = await pasos.GuardarAsync(TipoPasoTerminos, new PasoTerminos(usuario.Id, metodo, usuario.SecurityStamp ?? string.Empty));
            return Results.Json(new TerminosPendientes("terminos_pendientes", estado, pendientes), statusCode: StatusCodes.Status409Conflict);
        }
        await sesion.IniciarAsync(http, usuario, metodo, ct);
        return Results.NoContent();
    }

    // --- Recuperación ---

    private static async Task<IResult> SolicitarRecuperacionAsync([Description("Correo de la cuenta.")] SolicitudCorreo solicitud, CuentasExternas cuentas, HttpContext http, CancellationToken ct)
    {
        await cuentas.SolicitarRecuperacionAsync(solicitud.Correo, http, ct);
        return Results.Accepted();
    }

    private static async Task<IResult> ConsultarRecuperacionAsync(
        [Description("Enlace de recuperación.")] SolicitudEnlace solicitud, CuentasExternas cuentas, PlataformaDbContext plataforma, RegistroSeguridad registro, CancellationToken ct)
    {
        var usuario = await cuentas.UsuarioDeRecuperacionAsync(solicitud.Enlace);
        if (usuario is null)
        {
            await registro.RegistrarAsync(TiposEventoSeguridad.EnlaceRechazado, new DatosEventoSeguridad { Detalle = new { enlace = "recuperacion" } }, ct);
            return Error(StatusCodes.Status410Gone, CuentasExternas.EnlaceNoValido, "Este enlace ya se usó o venció. Pide uno nuevo desde la pantalla de ingreso.");
        }
        var nombre = await plataforma.Personas.Where(p => p.Id == usuario.PersonaId).Select(p => p.Nombre).SingleAsync(ct);
        return Results.Ok(new CuentaARecuperar(usuario.Email!, nombre));
    }

    private static async Task<IResult> OpcionesPasskeyRecuperacionAsync(
        [Description("Enlace de recuperación.")] SolicitudEnlace solicitud, CuentasExternas cuentas, FactoresExternos factores, PlataformaDbContext plataforma, HttpContext http, CancellationToken ct)
    {
        var usuario = await cuentas.UsuarioDeRecuperacionAsync(solicitud.Enlace);
        if (usuario is null)
        {
            return Error(StatusCodes.Status410Gone, CuentasExternas.EnlaceNoValido, "Este enlace ya se usó o venció.");
        }
        var nombre = await plataforma.Personas.Where(p => p.Id == usuario.PersonaId).Select(p => p.Nombre).SingleAsync(ct);
        return Results.Ok(await factores.OpcionesRegistroAsync(usuario.Id, usuario.Email!, nombre, CuentasExternas.AmbitoRecuperacion(usuario.Id), http));
    }

    private static async Task<IResult> ClaveTotpRecuperacionAsync([Description("Enlace de recuperación.")] SolicitudEnlace solicitud, CuentasExternas cuentas, FactoresExternos factores)
    {
        var usuario = await cuentas.UsuarioDeRecuperacionAsync(solicitud.Enlace);
        return usuario is null
            ? Error(StatusCodes.Status410Gone, CuentasExternas.EnlaceNoValido, "Este enlace ya se usó o venció.")
            : Results.Ok(await factores.NuevaClaveTotpAsync(usuario.Email!, CuentasExternas.AmbitoRecuperacion(usuario.Id)));
    }

    private static async Task<IResult> CompletarRecuperacionAsync(
        [Description("Enlace de recuperación y factor nuevo.")] SolicitudCompletarRecuperacion solicitud, CuentasExternas cuentas, SesionExterna sesion, HttpContext http, CancellationToken ct) =>
        Respuesta(await cuentas.CompletarRecuperacionAsync(solicitud.Enlace, solicitud.Factor, http, sesion, ct));
}
