using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class PaginacionRecepcionTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public PaginacionRecepcionTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));
        return client;
    }

    // =========================================================================
    // 1. GET /api/recepciones
    // =========================================================================

    [Fact]
    public async Task GetRecepciones_PaginacionValida_DevuelveHeadersYElementosCorrectos()
    {
        var obraId = Guid.NewGuid();
        var ocId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            for (int i = 1; i <= 5; i++)
            {
                var r = new RecepcionEntity
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    OrdenCompraId = ocId,
                    RecepcionadoPorUsuarioId = Guid.NewGuid(),
                    NumeroRemito = $"REM-PAG-{obraId.ToString()[..4]}-{i}",
                    FechaRecepcion = DateTime.UtcNow.AddMinutes(-i),
                    Estado = EstadoRecepcion.Pendiente
                };
                r.Detalles.Add(new DetalleRecepcion
                {
                    Id = Guid.NewGuid(),
                    RecepcionId = r.Id,
                    ArticuloId = Guid.NewGuid(),
                    DescripcionArticulo = "Material",
                    CantidadEsperada = 10,
                    CantidadRecibida = 10,
                    UnidadMedida = "UN",
                    Conforme = true
                });
                db.Recepciones.Add(r);
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        // Página 1 con tamaño 2
        var resp1 = await client.GetAsync($"/api/recepciones?obraId={obraId}&pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("5", resp1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", resp1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", resp1.Headers.GetValues("X-Page-Size").Single());

        var items1 = await resp1.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        Assert.NotNull(items1);
        Assert.Equal(2, items1.Count);

        // Página 2 con tamaño 2
        var resp2 = await client.GetAsync($"/api/recepciones?obraId={obraId}&pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("5", resp2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", resp2.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", resp2.Headers.GetValues("X-Page-Size").Single());

        var items2 = await resp2.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        Assert.NotNull(items2);
        Assert.Equal(2, items2.Count);

        // Página 3 con tamaño 2 (última página, solo 1 elemento)
        var resp3 = await client.GetAsync($"/api/recepciones?obraId={obraId}&pagina=3&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
        Assert.Equal("5", resp3.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("3", resp3.Headers.GetValues("X-Page").Single());

        var items3 = await resp3.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        Assert.NotNull(items3);
        Assert.Single(items3);

        // Los 5 elementos recibidos son únicos
        var todosIds = items1.Select(x => x.Id)
            .Concat(items2.Select(x => x.Id))
            .Concat(items3.Select(x => x.Id))
            .ToList();
        Assert.Equal(5, todosIds.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 10, "La página debe ser 1 o mayor.")]
    [InlineData(-1, 10, "La página debe ser 1 o mayor.")]
    [InlineData(1, 0, "El tamaño debe estar entre 1 y 50.")]
    [InlineData(1, 51, "El tamaño debe estar entre 1 y 50.")]
    public async Task GetRecepciones_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano, string detalleEsperado)
    {
        using var client = CreateAdminClient();
        var resp = await client.GetAsync($"/api/recepciones?pagina={pagina}&tamano={tamano}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paginación inválida", json.GetProperty("title").GetString());
        Assert.Equal(detalleEsperado, json.GetProperty("detail").GetString());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task GetRecepciones_OrdenEstableConMismaFecha_DesempataPorId()
    {
        var obraId = Guid.NewGuid();
        var ocId = Guid.NewGuid();
        var fechaFija = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        var idsCreados = new List<Guid>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            for (int i = 0; i < 4; i++)
            {
                var r = new RecepcionEntity
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    OrdenCompraId = ocId,
                    RecepcionadoPorUsuarioId = Guid.NewGuid(),
                    NumeroRemito = $"REM-STABLE-{i}",
                    FechaRecepcion = fechaFija,
                    Estado = EstadoRecepcion.Pendiente
                };
                idsCreados.Add(r.Id);
                db.Recepciones.Add(r);
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();
        var resp1 = await client.GetAsync($"/api/recepciones?obraId={obraId}&pagina=1&tamano=2");
        var resp2 = await client.GetAsync($"/api/recepciones?obraId={obraId}&pagina=2&tamano=2");

        var items1 = await resp1.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        var items2 = await resp2.Content.ReadFromJsonAsync<List<RecepcionDto>>();

        Assert.NotNull(items1);
        Assert.NotNull(items2);
        var todosIds = items1.Select(x => x.Id).Concat(items2.Select(x => x.Id)).ToList();
        Assert.Equal(4, todosIds.Distinct().Count());
        Assert.Equal(4, todosIds.Intersect(idsCreados).Count());
    }

    // =========================================================================
    // 2. GET /api/recepciones/ordenes-pendientes
    // =========================================================================

    [Fact]
    public async Task GetOrdenesPendientes_PaginacionValida_DevuelveHeadersYPaginasCorrectas()
    {
        var obraId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            for (int i = 1; i <= 4; i++)
            {
                var artId = Guid.NewGuid();
                var oc = OrdenCompra.Crear(
                    id: Guid.NewGuid(),
                    obraId: obraId,
                    proveedorId: Guid.NewGuid(),
                    montoTotal: 1000m * i,
                    items: new[] { OrdenCompraItem.Crear(artId, 20, "UN", 50, $"Item {i}") },
                    etapa: "Estructura",
                    fechaDespacho: DateTime.UtcNow);
                db.OrdenesCompra.Add(oc);
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        var resp1 = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}&pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("4", resp1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", resp1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", resp1.Headers.GetValues("X-Page-Size").Single());

        var items1 = await resp1.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(items1);
        Assert.Equal(2, items1.Count);

        var resp2 = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}&pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("4", resp2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", resp2.Headers.GetValues("X-Page").Single());

        var items2 = await resp2.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(items2);
        Assert.Equal(2, items2.Count);

        var todosIds = items1.Select(o => o.OrdenCompraId).Concat(items2.Select(o => o.OrdenCompraId)).ToList();
        Assert.Equal(4, todosIds.Distinct().Count());
    }

    [Fact]
    public async Task GetOrdenesPendientes_ConsultaSecundariaFiltraSoloIdsDePaginaActual()
    {
        var obraId = Guid.NewGuid();
        var oc1Id = Guid.NewGuid();
        var oc2Id = Guid.NewGuid();
        var art1 = Guid.NewGuid();
        var art2 = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

            var oc1 = OrdenCompra.Crear(oc1Id, obraId, Guid.NewGuid(), 1000,
                new[] { OrdenCompraItem.Crear(art1, 10, "UN", 100, "Articulo 1") },
                fechaDespacho: DateTime.UtcNow);
            var oc2 = OrdenCompra.Crear(oc2Id, obraId, Guid.NewGuid(), 2000,
                new[] { OrdenCompraItem.Crear(art2, 20, "UN", 100, "Articulo 2") },
                fechaDespacho: DateTime.UtcNow);

            db.OrdenesCompra.AddRange(oc1, oc2);

            // Recepción parcial para oc1
            var rec1 = new RecepcionEntity
            {
                Id = Guid.NewGuid(),
                ObraId = obraId,
                OrdenCompraId = oc1Id,
                RecepcionadoPorUsuarioId = Guid.NewGuid(),
                NumeroRemito = "REM-1",
                FechaRecepcion = DateTime.UtcNow,
                Estado = EstadoRecepcion.Parcial
            };
            rec1.Detalles.Add(new DetalleRecepcion
            {
                Id = Guid.NewGuid(),
                RecepcionId = rec1.Id,
                ArticuloId = art1,
                DescripcionArticulo = "Articulo 1",
                CantidadEsperada = 10,
                CantidadRecibida = 4,
                UnidadMedida = "UN",
                Conforme = true
            });
            db.Recepciones.Add(rec1);

            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        // Pedir página de tamaño 1 (obtiene la primera orden)
        var resp = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}&pagina=1&tamano=1");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("2", resp.Headers.GetValues("X-Total-Count").Single());

        var items = await resp.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(items);
        var unica = Assert.Single(items);
        Assert.Single(unica.Items);

        // Si la orden de la página 1 es oc1, la cantidad recibida refleja los 4 recibidos
        if (unica.OrdenCompraId == oc1Id)
        {
            Assert.Equal(4, unica.Items[0].CantidadRecibida);
            Assert.Equal(10, unica.Items[0].CantidadEsperada);
        }
        else
        {
            Assert.Equal(0, unica.Items[0].CantidadRecibida);
            Assert.Equal(20, unica.Items[0].CantidadEsperada);
        }
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetOrdenesPendientes_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano)
    {
        using var client = CreateAdminClient();
        var resp = await client.GetAsync($"/api/recepciones/ordenes-pendientes?pagina={pagina}&tamano={tamano}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paginación inválida", json.GetProperty("title").GetString());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task GetOrdenesPendientes_FiltroSinPartida_RespetaTotalFiltrado()
    {
        var obraId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            var ocNormal = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), 100,
                new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 5, "UN", 20, "Item A") },
                fechaDespacho: DateTime.UtcNow);
            var ocSinPartida1 = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), 200,
                new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 10, "UN", 20, "Item B") }, sinPartidaPresupuestaria: true,
                fechaDespacho: DateTime.UtcNow);
            var ocSinPartida2 = OrdenCompra.Crear(Guid.NewGuid(), obraId, Guid.NewGuid(), 300,
                new[] { OrdenCompraItem.Crear(Guid.NewGuid(), 15, "UN", 20, "Item C") }, sinPartidaPresupuestaria: true,
                fechaDespacho: DateTime.UtcNow);

            db.OrdenesCompra.AddRange(ocNormal, ocSinPartida1, ocSinPartida2);
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        var resp = await client.GetAsync($"/api/recepciones/ordenes-pendientes?obraId={obraId}&sinPartida=true&pagina=1&tamano=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("2", resp.Headers.GetValues("X-Total-Count").Single());

        var items = await resp.Content.ReadFromJsonAsync<List<OrdenPendienteDto>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.SinPartidaPresupuestaria));
    }

    // =========================================================================
    // 3. GET /api/presupuestos/partidas
    // =========================================================================

    [Fact]
    public async Task GetPartidas_PaginacionValida_DevuelveHeadersYResultadosPaginados()
    {
        var obraId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            for (int i = 1; i <= 5; i++)
            {
                db.PartidasPresupuestarias.Add(new PartidaPresupuestaria
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    CodigoPartida = $"P-{obraId.ToString()[..4]}-{i:D2}",
                    Descripcion = $"Partida {i}",
                    Etapa = "Estructura",
                    MontoAsignado = 50_000m * i,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        // Página 1 de 2
        var resp1 = await client.GetAsync($"/api/presupuestos/partidas?obraId={obraId}&pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal("5", resp1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", resp1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", resp1.Headers.GetValues("X-Page-Size").Single());

        var items1 = await resp1.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(items1);
        Assert.Equal(2, items1.Count);

        // Página 2 de 2
        var resp2 = await client.GetAsync($"/api/presupuestos/partidas?obraId={obraId}&pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.Equal("5", resp2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", resp2.Headers.GetValues("X-Page").Single());

        var items2 = await resp2.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(items2);
        Assert.Equal(2, items2.Count);

        // Página 3 de 2 (último elemento)
        var resp3 = await client.GetAsync($"/api/presupuestos/partidas?obraId={obraId}&pagina=3&tamano=2");
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
        Assert.Equal("5", resp3.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("3", resp3.Headers.GetValues("X-Page").Single());

        var items3 = await resp3.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(items3);
        Assert.Single(items3);

        var todosCodigos = items1.Select(p => p.CodigoPartida)
            .Concat(items2.Select(p => p.CodigoPartida))
            .Concat(items3.Select(p => p.CodigoPartida))
            .ToList();
        Assert.Equal(5, todosCodigos.Distinct().Count());
    }

    [Fact]
    public async Task GetPartidas_FiltroPorEtapa_ReflejaTotalNormalizado()
    {
        var obraId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.PartidasPresupuestarias.AddRange(
                new PartidaPresupuestaria
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    CodigoPartida = $"FUND-01-{Guid.NewGuid():N}"[..12],
                    Descripcion = "Cimientos",
                    Etapa = "Fundación",
                    MontoAsignado = 100_000m,
                    CreatedAt = DateTime.UtcNow
                },
                new PartidaPresupuestaria
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    CodigoPartida = $"FUND-02-{Guid.NewGuid():N}"[..12],
                    Descripcion = "Pilotes",
                    Etapa = "Fundacion",
                    MontoAsignado = 150_000m,
                    CreatedAt = DateTime.UtcNow
                },
                new PartidaPresupuestaria
                {
                    Id = Guid.NewGuid(),
                    ObraId = obraId,
                    CodigoPartida = $"ESTR-01-{Guid.NewGuid():N}"[..12],
                    Descripcion = "Vigas",
                    Etapa = "Estructura",
                    MontoAsignado = 200_000m,
                    CreatedAt = DateTime.UtcNow
                }
            );
            await db.SaveChangesAsync();
        }

        using var client = CreateAdminClient();

        // Filtrar por etapa "fundación" (con tilde) debería matchear 2 partidas ignorando tildes y mayúsculas
        var resp = await client.GetAsync($"/api/presupuestos/partidas?obraId={obraId}&etapa=fundacion&pagina=1&tamano=10");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("2", resp.Headers.GetValues("X-Total-Count").Single());

        var items = await resp.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetPartidas_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano)
    {
        using var client = CreateAdminClient();
        var resp = await client.GetAsync($"/api/presupuestos/partidas?pagina={pagina}&tamano={tamano}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paginación inválida", json.GetProperty("title").GetString());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
    }
}
