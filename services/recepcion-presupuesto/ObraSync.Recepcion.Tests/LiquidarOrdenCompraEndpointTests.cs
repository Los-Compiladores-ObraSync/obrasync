using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class LiquidarOrdenCompraEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public LiquidarOrdenCompraEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid OrdenCompraId, Guid ObraId, Guid ArticuloId)> SembrarOrdenAsync(
        EstadoOrdenCompra estado = EstadoOrdenCompra.Activa,
        decimal cantidadPedida = 100m,
        decimal montoTotal = 50_000m,
        decimal cantidadRecibida = 0m)
    {
        var obraId = Guid.NewGuid();
        var ocId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        var item = OrdenCompraItem.Crear(articuloId, cantidadPedida, "bolsas", 500m, "Cemento Portland x 50kg");

        var oc = OrdenCompra.Crear(
            id: ocId,
            obraId: obraId,
            proveedorId: Guid.NewGuid(),
            montoTotal: montoTotal,
            items: new[] { item },
            etapa: "Estructura",
            estado: estado,
            motivoBloqueo: estado == EstadoOrdenCompra.Bloqueada ? "Supera partida" : null,
            fechaDespacho: DateTime.UtcNow.AddDays(-1));

        db.OrdenesCompra.Add(oc);

        if (cantidadRecibida > 0m)
        {
            var recepcion = new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obraId,
                OrdenCompraId = ocId,
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = $"REM-{Guid.NewGuid().ToString()[..8]}",
                FechaRecepcion = DateTime.UtcNow,
                Estado = cantidadRecibida >= cantidadPedida ? EstadoRecepcion.Completa : EstadoRecepcion.Parcial,
                Detalles = new List<DetalleRecepcion>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = articuloId,
                        DescripcionArticulo = "Cemento Portland x 50kg",
                        CantidadEsperada = cantidadPedida,
                        CantidadRecibida = cantidadRecibida,
                        UnidadMedida = "bolsas",
                        Conforme = true
                    }
                }
            };

            db.Recepciones.Add(recepcion);
        }

        await db.SaveChangesAsync();
        return (ocId, obraId, articuloId);
    }

    [Fact]
    public async Task LiquidarOrden_SinToken_Devuelve401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync($"/api/recepciones/ordenes/{Guid.NewGuid()}/liquidar", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task LiquidarOrden_RolNoAutorizado_Devuelve403(string rol)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: rol));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{Guid.NewGuid()}/liquidar", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LiquidarOrden_Inexistente_Devuelve404ProblemDetails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{Guid.NewGuid()}/liquidar", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LiquidarOrden_SinRecepciones_BloqueaCon409ProblemDetails()
    {
        var (ocId, _, _) = await SembrarOrdenAsync(cantidadPedida: 100m, cantidadRecibida: 0m);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{ocId}/liquidar", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("saldo pendiente", body.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiquidarOrden_ConRecepcionParcial_BloqueaExcedenteCon409ProblemDetails()
    {
        var (ocId, _, _) = await SembrarOrdenAsync(cantidadPedida: 100m, cantidadRecibida: 60m);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{ocId}/liquidar", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("saldo pendiente", body.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiquidarOrden_BloqueadaPorDesvio_Devuelve409ProblemDetails()
    {
        var (ocId, _, _) = await SembrarOrdenAsync(
            estado: EstadoOrdenCompra.Bloqueada,
            cantidadPedida: 50m,
            cantidadRecibida: 50m);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{ocId}/liquidar", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LiquidarOrden_RecepcionCompleta_Devuelve200YRechazaSegundaLiquidacionCon409()
    {
        var (ocId, obraId, _) = await SembrarOrdenAsync(
            cantidadPedida: 100m,
            montoTotal: 85_000m,
            cantidadRecibida: 100m);

        var usuarioId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Comprador", userId: usuarioId));

        var response = await client.PostAsync($"/api/recepciones/ordenes/{ocId}/liquidar", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<OrdenCompraLiquidadaDto>();
        Assert.NotNull(dto);
        Assert.Equal(ocId, dto!.OrdenCompraId);
        Assert.Equal(obraId, dto.ObraId);
        Assert.Equal(85_000m, dto.MontoLiquidado);
        Assert.Equal(usuarioId, dto.LiquidadaPorUsuarioId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var ocDb = await db.OrdenesCompra.AsNoTracking().FirstAsync(o => o.Id == ocId);
            Assert.True(ocDb.EstaLiquidada);
            Assert.Equal(usuarioId, ocDb.LiquidadaPorUsuarioId);
            Assert.NotNull(ocDb.LiquidadaEn);
        }

        // Segundo intento: debe rechazar con 409 Conflict
        var segundoIntento = await client.PostAsync($"/api/recepciones/ordenes/{ocId}/liquidar", null);
        Assert.Equal(HttpStatusCode.Conflict, segundoIntento.StatusCode);
        Assert.Equal("application/problem+json", segundoIntento.Content.Headers.ContentType?.MediaType);
    }
}
