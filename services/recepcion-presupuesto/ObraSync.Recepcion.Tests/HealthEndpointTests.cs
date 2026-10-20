using System.Net;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class HealthEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public HealthEndpointTests(RecepcionApiFactory factory)
    {
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
