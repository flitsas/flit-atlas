using Flit.Atlas.Identidad.Datos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Flit.Atlas.Identidad.Emergencia;

/// <summary>
/// Cuenta de administrador de emergencia (sección <c>Identidad:Emergencia</c>, ATLAS-ADR-0027). No hay contraseña ni
/// enlace por defecto en ningún lado: la cuenta se crea y se activa con el comando de consola.
/// </summary>
public sealed class OpcionesEmergencia
{
    public const string Seccion = "Identidad:Emergencia";

    /// <summary>
    /// Correo ficticio de la cuenta: solo se usa al crearla (después la cuenta se reconoce por su asignación de
    /// emergencia, no por el correo). No debe ser un buzón real ni un correo del tenant de FLIT.
    /// </summary>
    public string Correo { get; set; } = "admin.emergencia@atlas.local";

    public string Nombre { get; set; } = "Administrador de emergencia";

    /// <summary>Cuánto vale el enlace de activación que imprime el comando (de un solo uso).</summary>
    public TimeSpan VigenciaEnlace { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>
/// Opciones del proveedor de tokens del enlace de activación: protector propio (un enlace de recuperación no sirve aquí ni
/// al revés) y vigencia de <see cref="OpcionesEmergencia.VigenciaEnlace"/>.
/// </summary>
public sealed class OpcionesTokenEmergencia : DataProtectionTokenProviderOptions
{
    public OpcionesTokenEmergencia()
    {
        Name = CuentaEmergencia.ProveedorToken;
        TokenLifespan = TimeSpan.FromHours(1);
    }
}

/// <summary>
/// Token del enlace de activación: el <see cref="DataProtectorTokenProvider{TUser}"/> de Identity (Data Protection, ligado
/// al sello de seguridad: al activar la cuenta o al volver a correr el comando, el sello cambia y el enlace deja de
/// servir).
/// </summary>
public sealed class ProveedorTokenEmergencia(
    IDataProtectionProvider proteccion,
    IOptions<OpcionesTokenEmergencia> opciones,
    ILogger<DataProtectorTokenProvider<UsuarioAtlas>> log)
    : DataProtectorTokenProvider<UsuarioAtlas>(proteccion, opciones, log);

/// <summary>Error esperado del comando (configuración que no sirve): se muestra el mensaje, sin traza.</summary>
public sealed class CuentaEmergenciaException(string mensaje) : InvalidOperationException(mensaje);
