namespace Flit.Atlas.Identidad.Personas;

/// <summary>Qué personas de Entra ID trae la sincronización diaria (ATLAS-ADR-0030).</summary>
public enum OrigenPersonas
{
    /// <summary>
    /// Por defecto: solo refresca a los empleados que Atlas ya tiene enlazados a Entra (entraron alguna vez). No crea
    /// personas: así una configuración vacía nunca trae cuentas de servicio, buzones compartidos ni salas.
    /// </summary>
    Enlazadas,

    /// <summary>Los miembros (transitivos) del grupo <see cref="OpcionesSincronizacionPersonas.GrupoEmpleados"/>.</summary>
    Grupo,

    /// <summary>Todos los usuarios miembro del tenant (<c>userType = Member</c>; nunca los invitados B2B).</summary>
    Tenant,
}

/// <summary>Sincronización diaria de personas con Microsoft Graph (sección <c>Identidad:Personas</c>, ATLAS-ADR-0030).</summary>
public sealed class OpcionesSincronizacionPersonas
{
    public const string Seccion = "Identidad:Personas";

    /// <summary>Apaga el trabajo programado (el botón «Sincronizar ahora» sigue funcionando). Solo aplica con Entra.</summary>
    public bool Habilitada { get; set; } = true;

    /// <summary>Hora del día, en la de Bogotá, a la que corre (por defecto 05:00).</summary>
    public TimeSpan Hora { get; set; } = new(5, 0, 0);

    public OrigenPersonas Origen { get; set; } = OrigenPersonas.Enlazadas;

    /// <summary>Object id del grupo de Entra con los empleados; obligatorio con <see cref="OrigenPersonas.Grupo"/>.</summary>
    public Guid? GrupoEmpleados { get; set; }

    /// <summary>Intentos adicionales si Graph o la base no responden.</summary>
    public int Reintentos { get; set; } = 4;

    /// <summary>Espera antes del primer reintento; se duplica en cada uno (2, 4, 8 y 16 min por defecto).</summary>
    public TimeSpan EsperaReintento { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Tope de la foto en bytes: una más grande no se guarda (queda el avatar con iniciales).</summary>
    public int TamanoMaximoFoto { get; set; } = 100 * 1024;

    /// <summary>
    /// Freno de seguridad: si una corrida fuera a dejar inactivas más de esta fracción de las personas activas enlazadas
    /// (y más de <see cref="MinimoFreno"/>), no deshabilita a nadie y lo deja en la auditoría para que alguien revise
    /// (p. ej. un registro de la app que apunta a otro tenant o un grupo vaciado por error).
    /// </summary>
    public double FraccionMaximaDeshabilitadas { get; set; } = 0.5;

    public int MinimoFreno { get; set; } = 3;

    internal void Validar()
    {
        if (Origen == OrigenPersonas.Grupo && GrupoEmpleados is null)
        {
            throw new InvalidOperationException(
                "Identidad__Personas__Origen=grupo exige Identidad__Personas__GrupoEmpleados (object id del grupo de Entra).");
        }
        if (Hora < TimeSpan.Zero || Hora >= TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException("Identidad__Personas__Hora va de 00:00 a 23:59.");
        }
        if (TamanoMaximoFoto is < 1 or > 1024 * 1024)
        {
            throw new InvalidOperationException("Identidad__Personas__TamanoMaximoFoto va de 1 byte a 1 MB.");
        }
    }
}
