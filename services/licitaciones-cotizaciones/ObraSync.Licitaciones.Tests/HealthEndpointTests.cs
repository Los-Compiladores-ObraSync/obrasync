using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // Program.cs exige Jwt:Secret; en las pruebas lo damos por variable de entorno
        Environment.SetEnvironmentVariable("Jwt__Secret", "clave-de-pruebas-de-al-menos-32-caracteres");
        _factory = factory;
    }

    [Fact]
    public async Task Health_responde_200_sin_token()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}