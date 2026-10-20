using System.Net;
using System.Text.Json;

namespace ObraSync.Gateway.Tests;

public class HealthTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public HealthTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Destino.Reiniciar();
    }

    [Fact]
    public async Task Health_responde_200_y_dice_que_el_gateway_esta_sano()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("gateway", json.RootElement.GetProperty("servicio").GetString());
    }

    [Fact]
    public async Task Health_lo_atiende_el_propio_gateway_y_no_se_reenvia_a_ningun_servicio()
    {
        var client = _factory.CreateClient();

        await client.GetAsync("/health");

        Assert.Null(_factory.Destino.UltimoPedido);
    }

    [Fact]
    public async Task Health_tambien_devuelve_el_correlation_id()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync("/health");

        Assert.True(respuesta.Headers.Contains("X-Correlation-ID"));
    }
}
