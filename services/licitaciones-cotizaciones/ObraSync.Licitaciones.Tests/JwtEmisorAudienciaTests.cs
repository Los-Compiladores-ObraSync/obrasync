using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>Punto 5 de #136: el servicio valida el emisor (iss) y la audiencia (aud) del token.</summary>
public class JwtEmisorAudienciaTests : IClassFixture<LicitacionesApiFactory>
{
    private readonly LicitacionesApiFactory _factory;

    public JwtEmisorAudienciaTests(LicitacionesApiFactory factory) => _factory = factory;

    private static string CrearToken(string? issuer, string? audience)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(LicitacionesApiFactory.Secret));
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", "comprador@obrasync.test"),
                new Claim("name", "Comprador"),
                new Claim("role", "Comprador")
            },
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<HttpStatusCode> ListarConAsync(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var respuesta = await client.GetAsync("/api/licitaciones");
        return respuesta.StatusCode;
    }

    [Fact]
    public async Task TokenConEmisorYAudienciaCorrectos_EsAceptado()
    {
        var status = await ListarConAsync(CrearToken("obrasync-usuarios", "obrasync-services"));

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Theory]
    [InlineData("otro-emisor", "obrasync-services")]
    [InlineData("obrasync-usuarios", "otra-audiencia")]
    [InlineData(null, null)]
    public async Task TokenConEmisorOAudienciaIncorrectos_Responde401(string? issuer, string? audience)
    {
        var status = await ListarConAsync(CrearToken(issuer, audience));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }
}
