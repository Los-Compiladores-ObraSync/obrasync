using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class ObtenerOrdenesBloqueadasEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public ObtenerOrdenesBloqueadasEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: role));
        return client;
    }

    private async Task<(Guid PartidaId, Guid OrdenBloqueadaId)> SembrarObraConOrdenBloqueadaAsync(
        decimal montoAsignado = 100_000m,
        decimal montoComprometido = 40_000m,
        decimal montoOcBloqueada = 75_000m,
        string? etapaPartida = "Estructura",
        string? etapaOrden = "Estructura",
        DateTime? fechaBloqueo = null,
        bool incluirOrdenActiva = false)
    {
        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();
        var ocBloqueadaId = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
        {
            Id = partidaId,
            ObraId = obraId,
            Etapa = etapaPartida,
            CodigoPartida = $"PART-{Guid.NewGuid().ToString()[..6]}",
            Descripcion = "Partida de prueba",
            MontoAsignado = montoAsignado,
            MontoComprometido = montoComprometido,
            MontoEjecutado = 0m,
            CreatedAt = DateTime.UtcNow
        });

        var ocBloqueada = OrdenCompra.Crear(
            id: ocBloqueadaId,
            obraId: obraId,
            proveedorId: Guid.NewGuid(),
            montoTotal: montoOcBloqueada,
            items: new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UN", montoOcBloqueada / 10m, "Material") },
            etapa: etapaOrden,
            estado: EstadoOrdenCompra.Bloqueada,
            motivoBloqueo: "Supera la partida asignada (#95)",
            fechaBloqueo: fechaBloqueo ?? DateTime.UtcNow);

        db.OrdenesCompra.Add(ocBloqueada);

        if (incluirOrdenActiva)
        {
            var ocActiva = OrdenCompra.Crear(
                id: Guid.NewGuid(),
                obraId: obraId,
                proveedorId: Guid.NewGuid(),
                montoTotal: 5_000m,
                items: new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 5m, "UN", 1_000m, "Material Activo") },
                etapa: etapaOrden,
                estado: EstadoOrdenCompra.Activa);
            db.OrdenesCompra.Add(ocActiva);
        }

        await db.SaveChangesAsync();
        return (partidaId, ocBloqueadaId);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_SinToken_Retorna401Unauthorized()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/recepciones/ordenes-bloqueadas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_TokenExpirado_Retorna401Unauthorized()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: "Admin", expired: true));

        var response = await client.GetAsync("/api/recepciones/ordenes-bloqueadas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task GetOrdenesBloqueadas_ConRolesNoAutorizados_Retorna403Forbidden(string rol)
    {
        using var client = CreateClientWithRole(rol);
        var response = await client.GetAsync("/api/recepciones/ordenes-bloqueadas");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task GetOrdenesBloqueadas_ConRolesAutorizados_Retorna200OkYDatosPresupuestarios(string rol)
    {
        var fechaBloqueo = new DateTime(2026, 11, 17, 15, 30, 0, DateTimeKind.Utc);
        var (partidaId, ocBloqueadaId) = await SembrarObraConOrdenBloqueadaAsync(
            montoAsignado: 100_000m,
            montoComprometido: 40_000m,
            montoOcBloqueada: 75_000m,
            etapaPartida: "Estructura",
            etapaOrden: "  estructura ",
            fechaBloqueo: fechaBloqueo,
            incluirOrdenActiva: true);

        Guid obraId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            obraId = db.OrdenesCompra.Single(o => o.Id == ocBloqueadaId).ObraId;
        }

        using var client = CreateClientWithRole(rol);
        var response = await client.GetAsync($"/api/recepciones/ordenes-bloqueadas?obraId={obraId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Page").Single());
        Assert.Equal("50", response.Headers.GetValues("X-Page-Size").Single());

        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var elemento = Assert.Single(doc.RootElement.EnumerateArray());

        // Verificar contrato exacto camelCase (#210)
        Assert.Equal(ocBloqueadaId, elemento.GetProperty("ordenCompraId").GetGuid());
        Assert.Equal(obraId, elemento.GetProperty("obraId").GetGuid());
        Assert.Equal(partidaId, elemento.GetProperty("partidaId").GetGuid());
        Assert.Equal("estructura", elemento.GetProperty("etapa").GetString());
        Assert.Equal(75_000m, elemento.GetProperty("montoOrdenCompra").GetDecimal());
        Assert.Equal(100_000m, elemento.GetProperty("montoAsignadoPartida").GetDecimal());
        Assert.Equal(40_000m, elemento.GetProperty("montoComprometidoActual").GetDecimal());
        Assert.Equal(115_000m, elemento.GetProperty("montoProyectado").GetDecimal());
        Assert.Equal(15_000m, elemento.GetProperty("exceso").GetDecimal());
        Assert.Equal("Supera la partida asignada (#95)", elemento.GetProperty("motivo").GetString());

        var dtos = JsonSerializer.Deserialize<List<OrdenBloqueadaDto>>(
            rawJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(dtos);
        var dto = Assert.Single(dtos);
        Assert.Equal(DateTimeKind.Utc, dto.FechaBloqueo.Kind);
        Assert.Equal(fechaBloqueo, dto.FechaBloqueo);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_ConsumidorBloqueaOc_ReflejaMismosDatosQueOrdenCompraBloqueadaEvent()
    {
        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();
        var ocId = Guid.NewGuid();
        OrdenCompraBloqueadaEvent? eventoPublicado = null;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = obraId,
                Etapa = "Instalación Eléctrica",
                CodigoPartida = $"ELEC-{Guid.NewGuid().ToString()[..6]}",
                Descripcion = "Instalación eléctrica",
                MontoAsignado = 50_000m,
                MontoComprometido = 20_000m,
                MontoEjecutado = 0m
            });
            await db.SaveChangesAsync();

            var consumer = new OrdenCompraEmitidaConsumer(
                db,
                new OrdenCompraRepository(db),
                NullLogger<OrdenCompraEmitidaConsumer>.Instance);

            var eventoEmitido = new OrdenCompraEmitidaEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: DateTime.UtcNow,
                CorrelationId: Guid.NewGuid(),
                OrdenCompraId: ocId,
                LicitacionId: Guid.NewGuid(),
                RequisicionId: Guid.NewGuid(),
                ObraId: obraId,
                ProveedorId: Guid.NewGuid(),
                MontoTotal: 45_000m, // 20k + 45k = 65k > 50k -> exceso 15k
                Items: new List<OrdenCompraEmitidaItemEvent>
                {
                    new(Guid.NewGuid(), 10m, "UN", 4_500m, "Tablero")
                },
                Etapa: "instalacion electrica");

            var consumeContext = Substitute.For<ConsumeContext<OrdenCompraEmitidaEvent>>();
            consumeContext.Message.Returns(eventoEmitido);
            consumeContext.CancellationToken.Returns(CancellationToken.None);
            consumeContext
                .Publish(Arg.Do<OrdenCompraBloqueadaEvent>(ev => eventoPublicado = ev), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            await consumer.Consume(consumeContext);
        }

        Assert.NotNull(eventoPublicado);

        using var client = CreateClientWithRole("Comprador");
        var lista = await client.GetFromJsonAsync<List<OrdenBloqueadaDto>>(
            $"/api/recepciones/ordenes-bloqueadas?obraId={obraId}");

        Assert.NotNull(lista);
        var item = Assert.Single(lista);
        Assert.Equal(eventoPublicado!.OrdenCompraId, item.OrdenCompraId);
        Assert.Equal(eventoPublicado.ObraId, item.ObraId);
        Assert.Equal(eventoPublicado.PartidaId, item.PartidaId);
        Assert.Equal(eventoPublicado.Etapa, item.Etapa);
        Assert.Equal(eventoPublicado.MontoOrdenCompra, item.MontoOrdenCompra);
        Assert.Equal(eventoPublicado.MontoAsignadoPartida, item.MontoAsignadoPartida);
        Assert.Equal(eventoPublicado.MontoComprometidoActual, item.MontoComprometidoActual);
        Assert.Equal(eventoPublicado.MontoProyectado, item.MontoProyectado);
        Assert.Equal(eventoPublicado.Exceso, item.Exceso);
        Assert.Equal(eventoPublicado.Motivo, item.Motivo);
        Assert.Equal(DateTimeKind.Utc, item.FechaBloqueo.Kind);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_TrasLiberarUnaOc_DesapareceYActualizaMontoComprometidoActualDeLasRestantes()
    {
        var obraId = Guid.NewGuid();
        var partidaId = Guid.NewGuid();
        var oc1Id = Guid.NewGuid();
        var oc2Id = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = partidaId,
                ObraId = obraId,
                Etapa = "Cimientos",
                CodigoPartida = $"CIM-{Guid.NewGuid().ToString()[..6]}",
                Descripcion = "Cimientos",
                MontoAsignado = 100_000m,
                MontoComprometido = 40_000m,
                MontoEjecutado = 0m
            });

            db.OrdenesCompra.AddRange(
                OrdenCompra.Crear(
                    oc1Id, obraId, Guid.NewGuid(), 75_000m,
                    new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UN", 7_500m, "Hierro") },
                    etapa: "Cimientos",
                    estado: EstadoOrdenCompra.Bloqueada,
                    motivoBloqueo: "Supera partida #1",
                    fechaBloqueo: DateTime.UtcNow.AddMinutes(-10)),
                OrdenCompra.Crear(
                    oc2Id, obraId, Guid.NewGuid(), 30_000m,
                    new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10m, "UN", 3_000m, "Cemento") },
                    etapa: "Cimientos",
                    estado: EstadoOrdenCompra.Bloqueada,
                    motivoBloqueo: "Supera partida #2",
                    fechaBloqueo: DateTime.UtcNow.AddMinutes(-5)));

            await db.SaveChangesAsync();
        }

        using var adminClient = CreateClientWithRole("Admin");

        // Antes de liberar: 2 bloqueadas
        var antes = await adminClient.GetFromJsonAsync<List<OrdenBloqueadaDto>>(
            $"/api/recepciones/ordenes-bloqueadas?obraId={obraId}");
        Assert.NotNull(antes);
        Assert.Equal(2, antes.Count);

        // Admin libera OC1 -> comprometido pasa de 40k a 115k (disponible = -15k intencional, #210)
        var liberarResp = await adminClient.PostAsJsonAsync(
            $"/api/recepciones/ordenes/{oc1Id}/liberar",
            new LiberarOrdenCompraRequest { Motivo = "Autorizado por gerencia" });
        Assert.Equal(HttpStatusCode.OK, liberarResp.StatusCode);

        // Después de liberar: solo queda OC2, y refleja montoComprometidoActual = 115.000
        using var compradorClient = CreateClientWithRole("Comprador");
        var respDespues = await compradorClient.GetAsync($"/api/recepciones/ordenes-bloqueadas?obraId={obraId}");
        Assert.Equal(HttpStatusCode.OK, respDespues.StatusCode);
        Assert.Equal("1", respDespues.Headers.GetValues("X-Total-Count").Single());

        var despues = await respDespues.Content.ReadFromJsonAsync<List<OrdenBloqueadaDto>>();
        Assert.NotNull(despues);
        var restante = Assert.Single(despues);
        Assert.Equal(oc2Id, restante.OrdenCompraId);
        Assert.Equal(115_000m, restante.MontoComprometidoActual);
        Assert.Equal(145_000m, restante.MontoProyectado);
        Assert.Equal(45_000m, restante.Exceso);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_UsaRespaldoPartidaGeneral_YSoportaOrdenSinPartida()
    {
        var obraConGeneral = Guid.NewGuid();
        var partidaGeneralId = Guid.NewGuid();
        var ocConGeneralId = Guid.NewGuid();

        var obraSinPartida = Guid.NewGuid();
        var ocSinPartidaId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = partidaGeneralId,
                ObraId = obraConGeneral,
                Etapa = null,
                CodigoPartida = $"GEN-{Guid.NewGuid().ToString()[..6]}",
                Descripcion = "Presupuesto General",
                MontoAsignado = 80_000m,
                MontoComprometido = 50_000m,
                MontoEjecutado = 0m
            });

            db.OrdenesCompra.AddRange(
                OrdenCompra.Crear(
                    ocConGeneralId, obraConGeneral, Guid.NewGuid(), 40_000m,
                    new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 4m, "UN", 10_000m, "Arena") },
                    etapa: "Terminaciones",
                    estado: EstadoOrdenCompra.Bloqueada,
                    motivoBloqueo: "Supera partida general"),
                OrdenCompra.Crear(
                    ocSinPartidaId, obraSinPartida, Guid.NewGuid(), 12_000m,
                    new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 2m, "UN", 6_000m, "Pintura") },
                    etapa: "Pintura",
                    estado: EstadoOrdenCompra.Bloqueada,
                    motivoBloqueo: "Bloqueada sin partida actual"));

            await db.SaveChangesAsync();
        }

        using var client = CreateClientWithRole("Comprador");

        var respGeneral = await client.GetFromJsonAsync<List<OrdenBloqueadaDto>>(
            $"/api/recepciones/ordenes-bloqueadas?obraId={obraConGeneral}");
        Assert.NotNull(respGeneral);
        var dtoGeneral = Assert.Single(respGeneral);
        Assert.Equal(partidaGeneralId, dtoGeneral.PartidaId);
        Assert.Equal(80_000m, dtoGeneral.MontoAsignadoPartida);
        Assert.Equal(50_000m, dtoGeneral.MontoComprometidoActual);
        Assert.Equal(90_000m, dtoGeneral.MontoProyectado);
        Assert.Equal(10_000m, dtoGeneral.Exceso);

        var respSinPartida = await client.GetFromJsonAsync<List<OrdenBloqueadaDto>>(
            $"/api/recepciones/ordenes-bloqueadas?obraId={obraSinPartida}");
        Assert.NotNull(respSinPartida);
        var dtoSinPartida = Assert.Single(respSinPartida);
        Assert.Null(dtoSinPartida.PartidaId);
        Assert.Equal(0m, dtoSinPartida.MontoAsignadoPartida);
        Assert.Equal(0m, dtoSinPartida.MontoComprometidoActual);
        Assert.Equal(12_000m, dtoSinPartida.MontoProyectado);
        Assert.Equal(0m, dtoSinPartida.Exceso);
        Assert.Equal("Bloqueada sin partida actual", dtoSinPartida.Motivo);
    }

    [Fact]
    public async Task GetOrdenesBloqueadas_PaginacionYOrdenEstable_DevuelveHeadersYSinDuplicadosNiFaltantes()
    {
        var obraId = Guid.NewGuid();
        var idsCreados = new List<Guid>();
        var fechaEmpate = new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
            {
                Id = Guid.NewGuid(),
                ObraId = obraId,
                Etapa = "Albañilería",
                CodigoPartida = $"ALB-{Guid.NewGuid().ToString()[..6]}",
                Descripcion = "Albañilería",
                MontoAsignado = 10_000m,
                MontoComprometido = 0m
            });

            for (int i = 0; i < 5; i++)
            {
                var ocId = Guid.NewGuid();
                idsCreados.Add(ocId);
                db.OrdenesCompra.Add(OrdenCompra.Crear(
                    id: ocId,
                    obraId: obraId,
                    proveedorId: Guid.NewGuid(),
                    montoTotal: 15_000m + (i * 100m),
                    items: new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 1m, "UN", 15_000m + (i * 100m), $"Mat {i}") },
                    etapa: "Albañilería",
                    estado: EstadoOrdenCompra.Bloqueada,
                    motivoBloqueo: $"Bloqueo {i}",
                    // 3 órdenes con la misma fecha para probar desempate estable por Id
                    fechaBloqueo: i < 3 ? fechaEmpate : fechaEmpate.AddMinutes(i)));
            }

            await db.SaveChangesAsync();
        }

        using var client = CreateClientWithRole("Comprador");

        var resp1 = await client.GetAsync($"/api/recepciones/ordenes-bloqueadas?obraId={obraId}&pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("5", resp1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", resp1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", resp1.Headers.GetValues("X-Page-Size").Single());
        var pag1 = await resp1.Content.ReadFromJsonAsync<List<OrdenBloqueadaDto>>();
        Assert.NotNull(pag1);
        Assert.Equal(2, pag1.Count);

        var resp2 = await client.GetAsync($"/api/recepciones/ordenes-bloqueadas?obraId={obraId}&pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("5", resp2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", resp2.Headers.GetValues("X-Page").Single());
        var pag2 = await resp2.Content.ReadFromJsonAsync<List<OrdenBloqueadaDto>>();
        Assert.NotNull(pag2);
        Assert.Equal(2, pag2.Count);

        var resp3 = await client.GetAsync($"/api/recepciones/ordenes-bloqueadas?obraId={obraId}&pagina=3&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
        Assert.Equal("5", resp3.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("3", resp3.Headers.GetValues("X-Page").Single());
        var pag3 = await resp3.Content.ReadFromJsonAsync<List<OrdenBloqueadaDto>>();
        Assert.NotNull(pag3);
        var unicaPag3 = Assert.Single(pag3);

        var todosIds = pag1.Select(x => x.OrdenCompraId)
            .Concat(pag2.Select(x => x.OrdenCompraId))
            .Append(unicaPag3.OrdenCompraId)
            .ToList();

        Assert.Equal(5, todosIds.Distinct().Count());
        Assert.Equal(5, todosIds.Intersect(idsCreados).Count());
    }

    [Theory]
    [InlineData(0, 10, "La página debe ser 1 o mayor.")]
    [InlineData(-1, 10, "La página debe ser 1 o mayor.")]
    [InlineData(1, 0, "El tamaño debe estar entre 1 y 50.")]
    [InlineData(1, 51, "El tamaño debe estar entre 1 y 50.")]
    public async Task GetOrdenesBloqueadas_ParametrosPaginacionInvalidos_Retorna400ProblemDetails(
        int pagina,
        int tamano,
        string detalleEsperado)
    {
        using var client = CreateClientWithRole("Admin");
        var resp = await client.GetAsync($"/api/recepciones/ordenes-bloqueadas?pagina={pagina}&tamano={tamano}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paginación inválida", json.GetProperty("title").GetString());
        Assert.Equal(detalleEsperado, json.GetProperty("detail").GetString());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
    }
}
