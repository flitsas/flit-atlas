namespace Flit.Atlas.Modulos.Soporte.Dominio.Microsoft365;

/// <summary>Qué originó un aviso de Teams (HU 2.10 y, para los recordatorios, HU 2.11).</summary>
public enum TipoAvisoTeams
{
    GeneracionPublicada,
    CambioAprobado,
    Recordatorio,
}

/// <summary>Feed .ics de los turnos propios o de un equipo.</summary>
public enum TipoFeedIcs
{
    Personal,
    Equipo,
}

/// <summary>
/// Sección <c>Soporte:M365</c> (ATLAS-ADR-0033). Sin <see cref="Buzon"/> o sin Entra, el calendario compartido queda
/// apagado; sin la URL del canal en <c>Plataforma:Teams:Canales:&lt;CanalTeams&gt;</c>, los avisos de Teams también. Los
/// feeds .ics no dependen de Microsoft 365.
/// </summary>
public sealed class OpcionesM365Soporte
{
    public const string Seccion = "Soporte:M365";

    /// <summary>
    /// Buzón compartido dueño del calendario «Soporte TI sábados» (su UPN o su id de Entra). Es el organizador de los
    /// eventos: Exchange envía la invitación a cada asistente y aparece en su Outlook y en el calendario de Teams.
    /// </summary>
    public string? Buzon { get; set; }

    /// <summary>Apaga la sincronización del calendario aunque haya buzón (p. ej. mientras se revisan los permisos).</summary>
    public bool CalendarioHabilitado { get; set; } = true;

    /// <summary>Nombre para mostrar del calendario (pantalla y feeds .ics).</summary>
    public string NombreCalendario { get; set; } = "Soporte TI sábados";

    /// <summary>Canal de <c>Plataforma:Teams:Canales</c> donde salen los avisos del soporte.</summary>
    public string CanalTeams { get; set; } = "soporte";

    /// <summary>Cada cuánto revisa el trabajo lo pendiente (además de despertarse con cada cambio en esta réplica).</summary>
    public TimeSpan Intervalo { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Primera espera tras un fallo; se duplica en cada fallo seguido hasta <see cref="EsperaMaxima"/>.</summary>
    public TimeSpan EsperaReintento { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan EsperaMaxima { get; set; } = TimeSpan.FromMinutes(30);

    public int TamanoLote { get; set; } = 50;

    /// <summary>Un aviso de Teams que no salió en este tiempo se descarta (ya no es noticia).</summary>
    public TimeSpan VigenciaAviso { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Días hacia atrás y hacia adelante que trae un feed .ics.</summary>
    public int DiasFeedAtras { get; set; } = 90;

    public int DiasFeedAdelante { get; set; } = 400;

    public bool CalendarioConfigurado => CalendarioHabilitado && !string.IsNullOrWhiteSpace(Buzon);

    /// <summary>Espera antes del intento siguiente: la de <c>Retry-After</c> si el servidor la dio, o la exponencial.</summary>
    public TimeSpan EsperaTras(int intentos, TimeSpan? pedida = null)
    {
        if (pedida is { } p && p > TimeSpan.Zero)
        {
            return p > EsperaMaxima ? EsperaMaxima : p;
        }
        var espera = EsperaReintento * Math.Pow(2, Math.Clamp(intentos - 1, 0, 20));
        return espera > EsperaMaxima ? EsperaMaxima : espera;
    }
}
