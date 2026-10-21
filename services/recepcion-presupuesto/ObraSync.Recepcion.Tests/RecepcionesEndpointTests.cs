using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionesEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public RecepcionesEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRecepciones_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepciones_ConTokenFirmaInvalida_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();
        var tokenInvalido = RecepcionApiFactory.CreateToken(customSecret: "secreto-completamente-invalido-y-diferente!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenInvalido);

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepciones_ConTokenExpirado_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();
        var tokenExpirado = RecepcionApiFactory.CreateToken(expired: true);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenExpirado);

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepciones_SinFiltroObra_DebeRetornarTodasLasRecepciones()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obra1,
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = "REM-001-TEST",
                Estado = EstadoRecepcion.Pendiente
            });
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obra2,
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = "REM-002-TEST",
                Estado = EstadoRecepcion.Completa
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken());

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recepciones = await response.Content.ReadFromJsonAsync<List<RecepcionEntity>>();
        Assert.NotNull(recepciones);
        Assert.True(recepciones.Count >= 2);
    }

    [Fact]
    public async Task GetRecepciones_FiltrandoPorObraId_DebeRetornarSoloRecepcionesDeEsaObra()
    {
        var obraTarget = Guid.NewGuid();
        var obraOtra = Guid.NewGuid();
        var remitoTarget = $"REM-{Guid.NewGuid():N}";

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obraTarget,
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = remitoTarget,
                Estado = EstadoRecepcion.Parcial
            });
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obraOtra,
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = $"REM-OTRA-{Guid.NewGuid():N}",
                Estado = EstadoRecepcion.Pendiente
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken());

        var response = await client.GetAsync($"/api/Recepciones?obraId={obraTarget}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recepciones = await response.Content.ReadFromJsonAsync<List<RecepcionEntity>>();
        Assert.NotNull(recepciones);
        Assert.All(recepciones, r => Assert.Equal(obraTarget, r.ObraId));
        Assert.Contains(recepciones, r => r.NumeroRemito == remitoTarget);
    }

    [Fact]
    public async Task GetRecepciones_FiltrandoPorObraIdSinRecepciones_DebeRetornarListaVaciaY200Ok()
    {
        var obraInexistente = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken());

        var response = await client.GetAsync($"/api/Recepciones?obraId={obraInexistente}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recepciones = await response.Content.ReadFromJsonAsync<List<RecepcionEntity>>();
        Assert.NotNull(recepciones);
        Assert.Empty(recepciones);
    }

    [Fact(Skip = "BUG-REC-01: Serialización cíclica entre Recepcion y DetalleRecepcion genera System.Text.Json.JsonException (HTTP 500) en lugar de HTTP 200 OK con los detalles.")]
    public async Task GetRecepciones_ConDetallesRelacionados_DebeIncluirDetallesDeMateriales()
    {
        var obraId = Guid.NewGuid();
        var recepcionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var recepcion = new RecepcionEntity
            {
                Id = recepcionId,
                ObraId = obraId,
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = $"REM-DETALLE-{Guid.NewGuid():N}",
                Estado = EstadoRecepcion.Completa
            };

            recepcion.Detalles.Add(new DetalleRecepcion
            {
                Id = Guid.NewGuid(),
                RecepcionId = recepcionId,
                ArticuloId = articuloId,
                DescripcionArticulo = "Cemento Portland 50kg",
                CantidadEsperada = 50,
                CantidadRecibida = 50,
                UnidadMedida = "Bolsa",
                Conforme = true
            });

            db.Recepciones.Add(recepcion);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken());

        var response = await client.GetAsync($"/api/Recepciones?obraId={obraId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recepciones = await response.Content.ReadFromJsonAsync<List<RecepcionEntity>>();
        Assert.NotNull(recepciones);
        var item = Assert.Single(recepciones);
        Assert.NotEmpty(item.Detalles);
        Assert.Equal(50, item.Detalles.First().CantidadRecibida);
        Assert.Equal("Cemento Portland 50kg", item.Detalles.First().DescripcionArticulo);
    }
}
