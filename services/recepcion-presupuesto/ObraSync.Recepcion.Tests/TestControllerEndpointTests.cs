using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using ObraSync.Recepcion.Api.Controllers;

namespace ObraSync.Recepcion.Tests;

public class TestControllerEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public TestControllerEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private static PublishOrdenCompraRequest CrearSolicitudValida()
    {
        return new PublishOrdenCompraRequest
        {
            OrdenCompraId = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            ProveedorId = Guid.NewGuid(),
            MontoTotal = 5000m,
            Items = new List<PublishOrdenCompraItemRequest>
            {
                new()
                {
                    ArticuloId = Guid.NewGuid(),
                    Cantidad = 50m,
                    UnidadMedida = "UNIDAD",
                    PrecioUnitario = 100m,
                    Descripcion = "Hierro 12mm"
                }
            }
        };
    }

    [Fact]
    public async Task PublishOrdenCompra_EnEntornoTesting_ConAdminYPayloadValido_Devuelve200Ok()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var request = CrearSolicitudValida();
        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PublishOrdenCompra_EnEntornoTesting_SinToken_Devuelve401Unauthorized()
    {
        using var client = _factory.CreateClient();
        var request = CrearSolicitudValida();

        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Comprador")]
    [InlineData("Proveedor")]
    public async Task PublishOrdenCompra_EnEntornoTesting_ConRolNoAdmin_Devuelve403Forbidden(string rol)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: rol));

        var request = CrearSolicitudValida();
        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishOrdenCompra_EnEntornoTesting_ConItemsVacios_Devuelve400BadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var request = CrearSolicitudValida();
        request.Items = new List<PublishOrdenCompraItemRequest>();

        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishOrdenCompra_EnEntornoTesting_ConObraVacia_Devuelve400BadRequest()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var request = CrearSolicitudValida();
        request.ObraId = Guid.Empty;

        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublishOrdenCompra_EnEntornoProduction_NoEstaRegistradoYDevuelve404NotFound()
    {
        // En entorno Production, SoloEntornoDesarrolloConvention remueve TestController
        using var prodFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
        });

        using var client = prodFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var request = CrearSolicitudValida();
        var response = await client.PostAsJsonAsync("/api/Test/publish-orden-compra", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
