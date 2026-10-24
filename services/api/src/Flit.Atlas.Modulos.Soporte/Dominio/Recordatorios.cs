namespace Flit.Atlas.Modulos.Soporte.Dominio;

/// <summary>Cuándo sale un recordatorio de turno (HU 2.11). En la base van como texto en snake_case.</summary>
public enum MomentoRecordatorio
{
    /// <summary>N días antes del sábado (la anticipación de la configuración), a la persona de turno.</summary>
    Anticipado,

    /// <summary>El mismo sábado: a cada persona de turno y al canal de Teams del equipo.</summary>
    MismoDia,
}

/// <summary>Por dónde sale: la bandeja de la app, push, correo, Teams a la persona o el canal de Teams del equipo.</summary>
public enum CanalRecordatorio
{
    App,
    Push,
    Correo,
    Teams,
    CanalTeams,
}

public enum EstadoRecordatorio
{
    EnCurso,
    Enviado,
    Fallido,
}

/// <summary>La corrida diaria a la hora configurada, o la que pidió alguien con «Enviar ahora».</summary>
public enum DisparoRecordatorios
{
    Programada,
    Manual,
}
