using System.Security.Claims;
using Flit.Atlas.Identidad;
using Flit.Atlas.Identidad.Entra;

namespace Flit.Atlas.Api.Tests;

public class ValidadorCuentaEntraTests
{
    private static readonly Guid TenantFlit = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly string Emisor = $"https://login.microsoftonline.com/{TenantFlit}/v2.0";

    private static ClaimsPrincipal Token(Guid? tid, string? idp = null, bool conOid = true)
    {
        var claims = new List<Claim> { new("iss", Emisor) };
        if (tid is not null)
        {
            claims.Add(new Claim("tid", tid.Value.ToString()));
        }
        if (idp is not null)
        {
            claims.Add(new Claim("idp", idp));
        }
        if (conOid)
        {
            claims.Add(new Claim("oid", Oid.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
    }

    [Fact]
    public void Acepta_un_empleado_del_tenant_de_FLIT()
    {
        var resultado = ValidadorCuentaEntra.Validar(Token(TenantFlit), TenantFlit);

        Assert.True(resultado.Valida);
        Assert.Equal(Oid, resultado.Oid);
    }

    [Fact]
    public void Rechaza_una_cuenta_personal_de_Microsoft() =>
        Assert.Equal("cuenta_personal", ValidadorCuentaEntra.Validar(Token(ValidadorCuentaEntra.TenantCuentasPersonales), TenantFlit).Motivo);

    [Fact]
    public void Rechaza_una_cuenta_de_otro_tenant() =>
        Assert.Equal("otro_tenant", ValidadorCuentaEntra.Validar(Token(Guid.NewGuid()), TenantFlit).Motivo);

    [Theory]
    [InlineData("live.com")]
    [InlineData("https://sts.windows.net/99999999-9999-9999-9999-999999999999/")]
    public void Rechaza_un_invitado_aunque_el_token_traiga_el_tenant_de_FLIT(string idp) =>
        Assert.Equal("invitado_externo", ValidadorCuentaEntra.Validar(Token(TenantFlit, idp), TenantFlit).Motivo);

    [Fact]
    public void Rechaza_un_token_sin_tenant_o_sin_oid()
    {
        Assert.Equal("sin_tenant", ValidadorCuentaEntra.Validar(Token(null), TenantFlit).Motivo);
        Assert.Equal("sin_oid", ValidadorCuentaEntra.Validar(Token(TenantFlit, conOid: false), TenantFlit).Motivo);
    }

    [Theory]
    [InlineData("/catalogo?x=1", "/catalogo?x=1")]
    [InlineData(null, "/")]
    [InlineData("https://sitio-ajeno.ejemplo.com", "/")]
    [InlineData("//sitio-ajeno.ejemplo.com", "/")]
    [InlineData("/\\sitio-ajeno.ejemplo.com", "/")]
    public void Solo_vuelve_a_rutas_locales(string? volver, string esperado) =>
        Assert.Equal(esperado, EndpointsIdentidad.RutaLocal(volver));
}
