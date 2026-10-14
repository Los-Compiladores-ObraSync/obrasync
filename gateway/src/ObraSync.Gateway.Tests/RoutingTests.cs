using System.Net;

namespace ObraSync.Gateway.Tests;

public class RoutingTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;

    public RoutingTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Destino.Reiniciar();
    }

    [Theory]
    [InlineData("/api/usuarios/prueba", 5001)]
    [InlineData("/api/obras/prueba", 5001)]
    [InlineData("/api/requisiciones/prueba", 5002)]
    [InlineData("/api/catalogo/prueba", 5002)]
    [InlineData("/api/licitaciones/prueba", 5003)]
    [InlineData("/api/cotizaciones/prueba", 5003)]
    [InlineData("/api/recepciones/prueba", 5004)]
    [InlineData("/api/presupuestos/prueba", 5004)]
    public async Task Cada_ruta_se_reenvia_al_servicio_correcto(string ruta, int puertoEsperado)
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var pedido = _factory.Destino.UltimoPedido;
        Assert.NotNull(pedido);
        Assert.Equal(puertoEsperado, pedido.Uri.Port);
        Assert.Equal(ruta, pedido.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Una_ruta_desconocida_devuelve_404_y_no_se_reenvia_a_ningun_servicio()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync("/api/inexistente/prueba");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Null(_factory.Destino.UltimoPedido);
    }
}
