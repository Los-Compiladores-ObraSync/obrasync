using System.Net;

namespace ObraSync.Gateway.Tests;

/// <summary>Hub de tiempo real del chat por orden (#225): se prueba por el Gateway y no solo directo al servicio.</summary>
public class ChatHubGatewayTests : IClassFixture<GatewayFactory>
{
    private const string Token = "jwt-secreto-del-hub-225";
    private readonly GatewayFactory _factory;

    public ChatHubGatewayTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Destino.Reiniciar();
    }

    [Fact]
    public async Task La_negociacion_llega_a_licitaciones_con_el_token_en_la_url_y_el_correlation_id()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1&access_token={Token}");
        request.Headers.Add("X-Correlation-ID", "hub-test-225");

        using var respuesta = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var pedido = _factory.Destino.UltimoPedido;
        Assert.NotNull(pedido);
        Assert.Equal(5003, pedido.Uri.Port);
        Assert.Equal("/api/ordenes-compra/chat-hub/negotiate", pedido.Uri.AbsolutePath);
        Assert.Contains($"access_token={Token}", pedido.Uri.Query);
        Assert.Equal("hub-test-225", pedido.CorrelationId);
    }

    [Fact]
    public async Task Sin_correlation_id_el_gateway_genera_uno_para_el_hub()
    {
        using var client = _factory.CreateClient();

        using var respuesta = await client.PostAsync("/api/ordenes-compra/chat-hub/negotiate?negotiateVersion=1", content: null);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(_factory.Destino.UltimoPedido?.CorrelationId));
    }

    [Fact]
    public async Task El_token_de_la_url_no_se_escribe_en_los_registros_del_gateway()
    {
        _factory.Logs.Limpiar();
        using var client = _factory.CreateClient();

        using var respuesta = await client.GetAsync($"/api/ordenes-compra/chat-hub?id=abc&access_token={Token}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain(Token, _factory.Logs.Texto);
        Assert.DoesNotContain(_factory.Logs.Entradas, e =>
            e.Categoria == "Yarp.ReverseProxy.Forwarder.HttpForwarder" && e.EventId.Id == 9);
    }

    [Theory]
    [InlineData("/api/ordenes-compra/chat-hub", true)]
    [InlineData("/api/ordenes-compra/chat-hub/negotiate", true)]
    [InlineData("/API/ORDENES-COMPRA/CHAT-HUB/negotiate", true)]
    [InlineData("/api/ordenes-compra/chat-hubs", false)]
    [InlineData("/api/ordenes-compra/123/chat/mensajes", false)]
    public void Solo_las_rutas_del_hub_se_tratan_como_hub(string ruta, bool esperado)
    {
        var contexto = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        contexto.Request.Path = ruta;

        Assert.Equal(esperado, ChatHubGatewayRequest.EsChatHub(contexto.Request));
    }
}
