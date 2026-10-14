namespace ObraSync.Gateway.Tests;

public class CorrelationIdTests : IClassFixture<GatewayFactory>
{
    private const string Header = "X-Correlation-ID";
    private const string Ruta = "/api/usuarios/prueba";

    private readonly GatewayFactory _factory;

    public CorrelationIdTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Destino.Reiniciar();
    }

    [Fact]
    public async Task Sin_encabezado_el_gateway_genera_un_id_y_lo_devuelve()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        var id = respuesta.Headers.GetValues(Header).Single();
        Assert.True(Guid.TryParse(id, out _));
    }

    [Fact]
    public async Task Con_encabezado_valido_el_gateway_lo_respeta()
    {
        var client = _factory.CreateClient();
        var pedido = new HttpRequestMessage(HttpMethod.Get, Ruta);
        pedido.Headers.Add(Header, "mi-prueba-123");

        var respuesta = await client.SendAsync(pedido);

        Assert.Equal("mi-prueba-123", respuesta.Headers.GetValues(Header).Single());
    }

    [Fact]
    public async Task Con_encabezado_demasiado_largo_el_gateway_lo_reemplaza()
    {
        var client = _factory.CreateClient();
        var idLargo = new string('a', 65);
        var pedido = new HttpRequestMessage(HttpMethod.Get, Ruta);
        pedido.Headers.Add(Header, idLargo);

        var respuesta = await client.SendAsync(pedido);

        var id = respuesta.Headers.GetValues(Header).Single();
        Assert.NotEqual(idLargo, id);
        Assert.True(Guid.TryParse(id, out _));
    }

    [Fact]
    public async Task El_servicio_de_destino_recibe_el_mismo_id_que_se_devuelve_al_cliente()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        var idDevuelto = respuesta.Headers.GetValues(Header).Single();
        Assert.Equal(idDevuelto, _factory.Destino.UltimoPedido?.CorrelationId);
    }

    [Fact]
    public async Task El_servicio_de_destino_recibe_el_id_que_mando_el_cliente()
    {
        var client = _factory.CreateClient();
        var pedido = new HttpRequestMessage(HttpMethod.Get, Ruta);
        pedido.Headers.Add(Header, "mi-prueba-123");

        await client.SendAsync(pedido);

        Assert.Equal("mi-prueba-123", _factory.Destino.UltimoPedido?.CorrelationId);
    }
}
