using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class PublicarLicitacionEndpointTests : IClassFixture<LicitacionesApiFactory>
{
    private readonly LicitacionesApiFactory _factory;

    public PublicarLicitacionEndpointTests(LicitacionesApiFactory factory) => _factory = factory;

    private HttpClient ClienteCon(string? rol)
    {
        var client = _factory.CreateClient();
        if (rol is not null)
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", LicitacionesApiFactory.CrearToken(rol));
        return client;
    }

    // Cada prueba siembra su propia requisición, así no se pisan entre sí
    private async Task<RequisicionAprobada> SembrarRequisicion()
    {
        var requisicionId = Guid.NewGuid();
        var requisicion = new RequisicionAprobada
        {
            RequisicionId = requisicionId,
            NumeroRequisicion = "REQ-TEST-001",
            ObraId = Guid.NewGuid(),
            RecibidaEnUtc = DateTime.UtcNow,
            Items = new List<RequisicionAprobadaItem>
            {
                new() { Id = Guid.NewGuid(), RequisicionId = requisicionId, ArticuloId = Guid.NewGuid(),
                        Descripcion = "Cemento", Cantidad = 80m, UnidadMedida = "BOLSA" }
            }
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        db.RequisicionesAprobadas.Add(requisicion);
        await db.SaveChangesAsync();
        return requisicion;
    }

    private static CrearLicitacionRequestDto Pedido(Guid requisicionId, DateTime? limite = null) => new()
    {
        RequisicionId = requisicionId,
        FechaLimiteCotizacion = limite ?? DateTime.UtcNow.AddDays(7)
    };

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var resp = await ClienteCon(null).PostAsJsonAsync("/api/licitaciones", Pedido(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task Roles_no_autorizados_responden_403(string rol)
    {
        var resp = await ClienteCon(rol).PostAsJsonAsync("/api/licitaciones", Pedido(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task Comprador_y_Admin_publican_la_licitacion_abierta_con_los_items_de_la_requisicion(string rol)
    {
        var requisicion = await SembrarRequisicion();

        var resp = await ClienteCon(rol).PostAsJsonAsync("/api/licitaciones", Pedido(requisicion.RequisicionId));

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var licitacion = await resp.Content.ReadFromJsonAsync<LicitacionResponseDto>();
        Assert.NotNull(licitacion);
        Assert.Equal("Abierta", licitacion!.Estado);
        Assert.Equal(requisicion.RequisicionId, licitacion.RequisicionId);
        Assert.Equal(requisicion.ObraId, licitacion.ObraId);
        Assert.Equal("REQ-TEST-001", licitacion.NumeroRequisicion);
        var item = Assert.Single(licitacion.Items);
        Assert.Equal("Cemento", item.Descripcion);
        Assert.Equal(80m, item.Cantidad);
    }

    [Fact]
    public async Task Requisicion_inexistente_responde_404()
    {
        var resp = await ClienteCon("Comprador").PostAsJsonAsync("/api/licitaciones", Pedido(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Fecha_limite_pasada_responde_400()
    {
        var requisicion = await SembrarRequisicion();

        var resp = await ClienteCon("Comprador").PostAsJsonAsync(
            "/api/licitaciones", Pedido(requisicion.RequisicionId, DateTime.UtcNow.AddMinutes(-5)));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Segunda_licitacion_abierta_para_la_misma_requisicion_responde_409()
    {
        var requisicion = await SembrarRequisicion();
        var cliente = ClienteCon("Comprador");

        var primera = await cliente.PostAsJsonAsync("/api/licitaciones", Pedido(requisicion.RequisicionId));
        var segunda = await cliente.PostAsJsonAsync("/api/licitaciones", Pedido(requisicion.RequisicionId));

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task Si_la_licitacion_anterior_esta_cerrada_se_puede_volver_a_licitar()
    {
        var requisicion = await SembrarRequisicion();
        var cliente = ClienteCon("Comprador");

        var primera = await cliente.PostAsJsonAsync("/api/licitaciones", Pedido(requisicion.RequisicionId));
        var creada = await primera.Content.ReadFromJsonAsync<LicitacionResponseDto>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var entidad = await db.Licitaciones.FindAsync(creada!.LicitacionId);
            entidad!.Estado = EstadoLicitacion.Cerrada;
            await db.SaveChangesAsync();
        }

        var segunda = await cliente.PostAsJsonAsync("/api/licitaciones", Pedido(requisicion.RequisicionId));

        Assert.Equal(HttpStatusCode.Created, segunda.StatusCode);
    }
}