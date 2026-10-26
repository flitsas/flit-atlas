using Flit.Atlas.Api.Tests.Exportaciones;
using Flit.Atlas.Modulos.Contratos.Datos;
using Flit.Atlas.Modulos.Contratos.Exportaciones;
using Flit.Atlas.Modulos.Contratos.Paquetes;

namespace Flit.Atlas.Api.Tests;

/// <summary>F4-SEC-23: el changelog de una exportación del portal solo nombra los contratos que se exportan a esa persona.</summary>
public class ChangelogExportacionTests(EntornoApi entorno)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_changelog_del_portal_no_nombra_contratos_fuera_del_alcance()
    {
        var cadena = entorno.Postgres.GetConnectionString();
        var visible = await ContratosDePrueba.SembrarAsync(cadena, ct: Ct);
        var ajeno = await ContratosDePrueba.SembrarAsync(cadena, ct: Ct);
        await using (var contexto = ContratosDePrueba.Contexto(cadena))
        {
            // Revisión 2 del paquete del contrato visible: entra el otro contrato (que esta persona no tiene en su alcance).
            var foto = new FotoPaquete("Integración ICT", null,
            [
                new EntradaPaquete(visible.ContratoId, visible.VersionId, ["GET /uno"], []),
                new EntradaPaquete(ajeno.ContratoId, ajeno.VersionId, ["GET /dos"], []),
            ]);
            contexto.RevisionesPaquete.Add(new RevisionPaquete
            {
                PaqueteId = visible.PaqueteId, Numero = 2, Nombre = foto.Nombre, Definicion = foto.DefinicionJson(), Nota = "Agrega otro contrato.",
                AutorPersonaId = Guid.NewGuid(), GuardadaEn = DateTimeOffset.UtcNow, Huella = foto.Huella(),
            });
            await contexto.SaveChangesAsync(Ct);
        }

        await using var lectura = ContratosDePrueba.Contexto(cadena);
        var completo = await ArmadorEntrada.ChangelogPaqueteAsync(lectura, visible.PaqueteId, 2, Ct);
        var portal = await ArmadorEntrada.ChangelogPaqueteAsync(lectura, visible.PaqueteId, 2, Ct, new HashSet<Guid> { visible.ContratoId });

        Assert.Contains(ajeno.Codigo, completo[0].Cambio, StringComparison.Ordinal);
        Assert.All(portal, e => Assert.DoesNotContain(ajeno.Codigo, e.Cambio, StringComparison.Ordinal));
    }
}
