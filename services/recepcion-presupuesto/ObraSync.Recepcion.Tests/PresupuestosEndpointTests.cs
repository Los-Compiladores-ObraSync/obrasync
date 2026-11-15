using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class PresupuestosEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public PresupuestosEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CrearPartida_SinToken_Retorna401Unauthorized()
    {
        var client = _factory.CreateClient();
        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = Guid.NewGuid(),
            CodigoPartida = "EST-01",
            Descripcion = "Estructura",
            MontoAsignado = 100_000m
        };

        var response = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task CrearPartida_ConRolNoAutorizado_Retorna403Forbidden(string rol)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: rol));

        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = Guid.NewGuid(),
            CodigoPartida = "EST-FORBIDDEN",
            Descripcion = "Estructura",
            MontoAsignado = 100_000m
        };

        var response = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Comprador")]
    public async Task CrearPartida_ConRolAutorizado_Retorna201Created(string rol)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: rol));

        var obraId = Guid.NewGuid();
        var codigo = $"PART-{Guid.NewGuid().ToString()[..6]}";
        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = codigo,
            Descripcion = "Hormigón elaborado",
            MontoAsignado = 250_000m,
            Moneda = "ARS"
        };

        var response = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creada = await response.Content.ReadFromJsonAsync<PartidaPresupuestariaDto>();
        Assert.NotNull(creada);
        Assert.Equal(obraId, creada.ObraId);
        Assert.Equal(codigo, creada.CodigoPartida);
        Assert.Equal("Estructura", creada.Etapa);
        Assert.Equal(250_000m, creada.MontoAsignado);
        Assert.Equal(250_000m, creada.MontoDisponible);
        Assert.Equal(0m, creada.MontoComprometido);
        Assert.Equal(0m, creada.MontoEjecutado);
    }

    [Fact]
    public async Task CrearPartida_CodigoDuplicado_Retorna409Conflict()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var obraId = Guid.NewGuid();
        var codigo = $"DUP-{Guid.NewGuid().ToString()[..6]}";
        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = obraId,
            CodigoPartida = codigo,
            Descripcion = "Primera carga",
            MontoAsignado = 100_000m
        };

        var res1 = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Intento duplicar para la misma obra y código
        var res2 = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task CrearPartida_MontoNegativo_Retorna400BadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var request = new CrearPartidaPresupuestariaRequest
        {
            ObraId = Guid.NewGuid(),
            CodigoPartida = "PART-NEG",
            Descripcion = "Partida inválida",
            MontoAsignado = -50m
        };

        var response = await client.PostAsJsonAsync("/api/presupuestos/partidas", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerPartidas_SinToken_Retorna401Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/presupuestos/partidas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerPartidas_ConTokenProveedor_Retorna403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Proveedor"));

        var response = await client.GetAsync("/api/presupuestos/partidas");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Comprador")]
    [InlineData("Capataz")]
    public async Task ObtenerPartidas_ConRolesLecturaPermitidos_Retorna200Ok(string rol)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: rol));

        var response = await client.GetAsync("/api/presupuestos/partidas");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ObtenerPartidas_ConFiltrosObraYEtapa_FiltraCorrectamente()
    {
        var obraTarget = Guid.NewGuid();
        var partidaId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.AddRange(
                new PartidaPresupuestaria
                {
                    Id = partidaId,
                    ObraId = obraTarget,
                    Etapa = "Fundaciones",
                    CodigoPartida = $"FUND-{Guid.NewGuid().ToString()[..6]}",
                    Descripcion = "Pilotes",
                    MontoAsignado = 300_000m,
                    MontoComprometido = 50_000m,
                    CreatedAt = DateTime.UtcNow
                },
                new PartidaPresupuestaria
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraTarget,
                    Etapa = "Instalaciones",
                    CodigoPartida = $"INST-{Guid.NewGuid().ToString()[..6]}",
                    Descripcion = "Plomería",
                    MontoAsignado = 150_000m,
                    CreatedAt = DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        // Filtrar por obra y etapa
        var response = await client.GetAsync($"/api/presupuestos/partidas?obraId={obraTarget}&etapa=Fundaciones");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = await response.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal(partidaId, list[0].Id);
        Assert.Equal(300_000m, list[0].MontoAsignado);
        Assert.Equal(50_000m, list[0].MontoComprometido);
        Assert.Equal(250_000m, list[0].MontoDisponible);
    }

    [Fact]
    public async Task ObtenerPartidaPorId_ExistenteEInexistente()
    {
        var partidaId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = Guid.NewGuid(),
                Etapa = "Mampostería",
                CodigoPartida = $"MAMP-{Guid.NewGuid().ToString()[..6]}",
                Descripcion = "Ladrillos",
                MontoAsignado = 80_000m,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Comprador"));

        var resOk = await client.GetAsync($"/api/presupuestos/partidas/{partidaId}");
        Assert.Equal(HttpStatusCode.OK, resOk.StatusCode);

        var detalle = await resOk.Content.ReadFromJsonAsync<PartidaPresupuestariaDto>();
        Assert.NotNull(detalle);
        Assert.Equal(partidaId, detalle.Id);
        Assert.Equal(80_000m, detalle.MontoDisponible);

        var resNotFound = await client.GetAsync($"/api/presupuestos/partidas/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resNotFound.StatusCode);
    }
}
