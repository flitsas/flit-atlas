using System.Net;
using Flit.Atlas.Plataforma.Entidades;
using Npgsql;

namespace Flit.Atlas.Plataforma.Tests;

/// <summary>CA de la HU 1.2: una escritura auditada no se puede actualizar ni borrar con el usuario de la aplicación.</summary>
public class AuditoriaTests(Contenedores contenedores)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EventoAuditoria Evento() => new()
    {
        OcurridoEn = DateTimeOffset.UtcNow,
        ActorTipo = TipoActor.Persona,
        ActorId = Guid.CreateVersion7().ToString(),
        Accion = "identidad.inicio_sesion",
        RecursoTipo = "sesion",
        Ip = IPAddress.Parse("192.0.2.10"), // TEST-NET-1 (RFC 5737): no es una IP real.
        Datos = """{"metodo":"entra"}""",
    };

    [Theory]
    [InlineData("UPDATE atlas.plataforma_auditoria SET accion = 'alterada'")]
    [InlineData("DELETE FROM atlas.plataforma_auditoria")]
    [InlineData("TRUNCATE atlas.plataforma_auditoria")]
    public async Task El_usuario_de_la_aplicacion_inserta_pero_no_puede_cambiar_ni_borrar(string sql)
    {
        var bd = await contenedores.CrearBaseAsync();
        await using (var contexto = bd.Contexto())
        {
            contexto.Auditoria.Add(Evento());
            await contexto.SaveChangesAsync(Ct);
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => Contenedores.EjecutarAsync(bd.Servicio, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        // Lo frena el permiso revocado (primera capa), antes de llegar al trigger.
        Assert.StartsWith("permission denied", error.MessageText, StringComparison.Ordinal);
        Assert.Equal(["identidad.inicio_sesion"], await MigracionesTests.ConsultarAsync(bd.Servicio, "SELECT accion FROM atlas.plataforma_auditoria"));
    }

    [Theory]
    [InlineData("UPDATE atlas.plataforma_auditoria SET accion = 'alterada'")]
    [InlineData("DELETE FROM atlas.plataforma_auditoria")]
    [InlineData("TRUNCATE atlas.plataforma_auditoria")]
    public async Task Ni_siquiera_un_superusuario_puede_cambiarla(string sql)
    {
        var bd = await contenedores.CrearBaseAsync();
        await using (var contexto = bd.Contexto())
        {
            contexto.Auditoria.Add(Evento());
            await contexto.SaveChangesAsync(Ct);
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => Contenedores.EjecutarAsync(bd.Admin, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Contains("solo inserción", error.MessageText, StringComparison.Ordinal);
    }
}
