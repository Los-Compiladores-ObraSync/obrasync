using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

public class RecepcionesPermisosLecturaTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public RecepcionesPermisosLecturaTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRecepciones_ConTokenProveedor_DebeRetornar403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Proveedor"));

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepciones_ConTokenComprador_DebeRetornar200Ok()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var response = await client.GetAsync("/api/Recepciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepcionPorId_ConTokenProveedor_DebeRetornar403Forbidden()
    {
        var recepcionId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = recepcionId,
                ObraId = Guid.NewGuid(),
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = "REM-PERM-TEST",
                Estado = EstadoRecepcion.Completa
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Proveedor"));

        var response = await client.GetAsync($"/api/Recepciones/{recepcionId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRecepcionPorId_ConTokenComprador_DebeRetornar200Ok()
    {
        var recepcionId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.Recepciones.Add(new RecepcionEntity
            {
                Id = recepcionId,
                ObraId = Guid.NewGuid(),
                OrdenCompraId = Guid.NewGuid(),
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = "REM-COMP-TEST",
                Estado = EstadoRecepcion.Completa
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var response = await client.GetAsync($"/api/Recepciones/{recepcionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
