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

namespace ObraSync.Recepcion.Tests;

public class LiberarOrdenCompraEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public LiberarOrdenCompraEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid OrdenCompraId, Guid ObraId, Guid PartidaId)> SembrarOrdenConPartidaAsync(
        EstadoOrdenCompra estado = EstadoOrdenCompra.Bloqueada,
        decimal montoAsignado = 100_000m,
        decimal montoComprometido = 40_000m,
        decimal montoOc = 75_000m)
    {
        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();
        var ocId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            Id = partidaId,
            ObraId = obraId,
            Etapa = "Estructura",
            CodigoPartida = $"EST-{Guid.NewGuid().ToString()[..6]}",
            Descripcion = "Hormigón armado",
            MontoAsignado = montoAsignado,
            MontoComprometido = montoComprometido,
            MontoEjecutado = 0m,
            CreatedAt = DateTime.UtcNow
        });

        var item = OrdenCompraItem.Crear(Guid.NewGuid(), 75m, "M3", 1000m, "Hormigón H30");
        var oc = OrdenCompra.Crear(
            id: ocId,
            obraId: obraId,
            proveedorId: Guid.NewGuid(),
            montoTotal: montoOc,
            items: new[] { item },
            etapa: "Estructura",
            estado: estado,
            motivoBloqueo: estado == EstadoOrdenCompra.Bloqueada ? "Supera la partida asignada (#95)" : null);

        db.OrdenesCompra.Add(oc);
        await db.SaveChangesAsync();

        return (ocId, obraId, partidaId);
    }

    [Fact]
    public async Task LiberarOrden_SinToken_Retorna401Unauthorized()
    {
        var (ocId, _, _) = await SembrarOrdenConPartidaAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/recepciones/ordenes/{ocId}/liberar",
            new LiberarOrdenCompraRequest { Motivo = "Intento sin token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task LiberarOrden_ConRolesNoAutorizados_Retorna403Forbidden(string rol)
    {
        var (ocId, _, _) = await SembrarOrdenConPartidaAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: rol));

        var response = await client.PostAsJsonAsync(
            $"/api/recepciones/ordenes/{ocId}/liberar",
            new LiberarOrdenCompraRequest { Motivo = "Intento con rol no Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LiberarOrden_ConRolAdmin_Retorna200_ActivaLaOrden_RegistraSubYFecha_SumaComprometido_YApareceEnOrdenesPendientes()
    {
        var (ocId, obraId, partidaId) = await SembrarOrdenConPartidaAsync(
            estado: EstadoOrdenCompra.Bloqueada,
            montoAsignado: 100_000m,
            montoComprometido: 40_000m,
            montoOc: 75_000m);

        var adminId = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: "Admin", userId: adminId));

        var antes = DateTime.UtcNow;

        // 1. Antes de liberar: NO aparece en ordenes-pendientes
        var pendientesAntes = await client.GetFromJsonAsync<List<OrdenPendienteDto>>(
            $"/api/Recepciones/ordenes-pendientes?obraId={obraId}");
        Assert.NotNull(pendientesAntes);
        Assert.DoesNotContain(pendientesAntes, o => o.OrdenCompraId == ocId);

        // 2. Admin libera la orden bloqueada
        var response = await client.PostAsJsonAsync(
            $"/api/recepciones/ordenes/{ocId}/liberar",
            new LiberarOrdenCompraRequest { Motivo = "Autorizado por Dirección de Obra" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<OrdenCompraLiberadaDto>();
        Assert.NotNull(dto);
        Assert.Equal(ocId, dto.OrdenCompraId);
        Assert.Equal(obraId, dto.ObraId);
        Assert.Equal(EstadoOrdenCompra.Activa, dto.Estado);
        Assert.Equal(adminId, dto.LiberadaPorUsuarioId);
        Assert.True(dto.LiberadaEn >= antes);
        Assert.Equal("Autorizado por Dirección de Obra", dto.MotivoLiberacion);
        Assert.Equal(75_000m, dto.MontoTotal);
        Assert.Equal(partidaId, dto.PartidaPresupuestariaId);
        Assert.Equal(115_000m, dto.MontoComprometidoPartida); // 40k + 75k

        // 3. Verificar en BD que quedaron persistidos quién, cuándo, estado Activa y el MontoComprometido
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var ocEnBd = await db.OrdenesCompra.SingleAsync(o => o.Id == ocId);
            Assert.Equal(EstadoOrdenCompra.Activa, ocEnBd.Estado);
            Assert.Null(ocEnBd.MotivoBloqueo);
            Assert.Equal(adminId, ocEnBd.LiberadaPorUsuarioId);
            Assert.NotNull(ocEnBd.LiberadaEn);
            Assert.Equal("Autorizado por Dirección de Obra", ocEnBd.MotivoLiberacion);

            var partidaEnBd = await db.PartidasPresupuestarias.SingleAsync(p => p.Id == partidaId);
            Assert.Equal(115_000m, partidaEnBd.MontoComprometido);
        }

        // 4. Después de liberar: ahora SÍ aparece en ordenes-pendientes para poder ser recepcionada
        var pendientesDespues = await client.GetFromJsonAsync<List<OrdenPendienteDto>>(
            $"/api/Recepciones/ordenes-pendientes?obraId={obraId}");
        Assert.NotNull(pendientesDespues);
        Assert.Contains(pendientesDespues, o => o.OrdenCompraId == ocId);
    }

    [Fact]
    public async Task LiberarOrden_OrdenInexistente_Retorna404NotFoundConProblemDetails()
    {
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsync(
            $"/api/recepciones/ordenes/{Guid.NewGuid()}/liberar",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, json.GetProperty("status").GetInt32());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task LiberarOrden_OrdenYaActivaNoBloqueada_Retorna409ConflictConProblemDetails()
    {
        var (ocId, _, _) = await SembrarOrdenConPartidaAsync(estado: EstadoOrdenCompra.Activa);
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsJsonAsync(
            $"/api/recepciones/ordenes/{ocId}/liberar",
            new LiberarOrdenCompraRequest { Motivo = "Ya estaba activa" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(409, json.GetProperty("status").GetInt32());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
    }
}
