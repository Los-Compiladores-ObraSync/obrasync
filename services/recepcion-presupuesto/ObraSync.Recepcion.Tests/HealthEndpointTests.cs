using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", "clave-de-pruebas-de-al-menos-32-caracteres");
        _factory = factory;
    }

    [Fact]
    public async Task Health_Responde_200_Ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
