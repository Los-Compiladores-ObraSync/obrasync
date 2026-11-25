using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class CalificacionPorOrdenYRecepcionCompletaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public CalificacionPorOrdenYRecepcionCompletaTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out string dbName,
        Action<LicitacionesDbContext>? seedAction = null)
    {
        dbName = $"DB_Licit_222_{Guid.NewGuid()}";
        var nombreDb = dbName;

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<LicitacionesDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IDbContextOptionsConfiguration<LicitacionesDbContext>>();

                services.AddDbContext<LicitacionesDbContext>(options =>
                {
                    options.UseInMemoryDatabase(nombreDb)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
                });

                if (seedAction != null)
                {
                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
                    seedAction(db);
                    db.SaveChanges();
                }
            });
        });
    }

    private static string GenerarTokenJwt(string role, Guid? userId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", $"{role.ToLowerInvariant()}@obrasync.com"),
            new Claim("name", $"Usuario {role}"),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task AssertProblemDetailsWithCorrelationIdAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCorrelationId)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal((int)expectedStatus, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCorrelationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    private static (Guid ordenId, Guid licitacionId, Guid proveedorId) SembrarOrden(
        LicitacionesDbContext db,
        Guid? proveedorId = null,
        bool despachada = true,
        bool recibidaCompleta = false)
    {
        var licId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var provId = proveedorId ?? Guid.NewGuid();
        var cotizId = Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Adjudicada,
            FechaCreacion = DateTime.UtcNow.AddDays(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(1)
        });

        db.OrdenesCompra.Add(new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = licId,
            CotizacionId = cotizId,
            ProveedorId = provId,
            Estado = despachada ? "Despachada" : "Emitida",
            FechaEmision = DateTime.UtcNow.AddDays(-1),
            FechaDespacho = despachada ? DateTime.UtcNow.AddHours(-6) : null,
            DespachadaPor = despachada ? provId : null,
            RecibidaCompleta = recibidaCompleta,
            FechaRecepcionCompleta = recibidaCompleta ? DateTime.UtcNow.AddHours(-1) : null
        });

        return (ordenId, licId, provId);
    }

    private static ConsumeContext<RecepcionConfirmadaEvent> CrearContextoEvento(RecepcionConfirmadaEvent evento)
    {
        var ctx = Substitute.For<ConsumeContext<RecepcionConfirmadaEvent>>();
        ctx.Message.Returns(evento);
        ctx.CorrelationId.Returns(evento.CorrelationId);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    // ---- 1. CotizacionAdjudicadaId en GET /api/licitaciones y GET /api/licitaciones/{id} ----

    [Fact]
    public async Task Licitacion_AntesYDespuesDeAdjudicar_ExponeCotizacionAdjudicadaIdEnDetalleYListado()
    {
        var licitacionId = Guid.NewGuid();
        var cotizacionGanadoraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-222-ADJ",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 15m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizacionGanadoraId,
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                Flete = 100m,
                PlazoEntregaDias = 2,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotizacionGanadoraId,
                        ArticuloId = articuloId,
                        PrecioUnitario = 1200m
                    }
                }
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // Antes de adjudicar -> cotizacionAdjudicadaId es null
        var detalleAntes = await client.GetFromJsonAsync<LicitacionResponseDto>($"/api/licitaciones/{licitacionId}");
        Assert.NotNull(detalleAntes);
        Assert.Null(detalleAntes.CotizacionAdjudicadaId);
        Assert.Null(detalleAntes.FechaAdjudicacion);

        // Adjudicar
        var antesDeAdjudicar = DateTime.UtcNow.AddSeconds(-2);
        var adjResp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/adjudicaciones",
            new { cotizacionId = cotizacionGanadoraId });
        Assert.Equal(HttpStatusCode.OK, adjResp.StatusCode);

        // Después de adjudicar -> detalle y listado incluyen cotizacionAdjudicadaId
        var detalleDespues = await client.GetFromJsonAsync<LicitacionResponseDto>($"/api/licitaciones/{licitacionId}");
        Assert.NotNull(detalleDespues);
        Assert.Equal("Adjudicada", detalleDespues.Estado);
        Assert.Equal(cotizacionGanadoraId, detalleDespues.CotizacionAdjudicadaId);
        Assert.NotNull(detalleDespues.FechaAdjudicacion);
        Assert.InRange(detalleDespues.FechaAdjudicacion!.Value, antesDeAdjudicar, DateTime.UtcNow.AddSeconds(2));
        Assert.Equal(DateTimeKind.Utc, detalleDespues.FechaAdjudicacion.Value.Kind);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacionId);
            Assert.Equal(detalleDespues.FechaAdjudicacion, persistida.FechaAdjudicacion);
        }

        var listado = await client.GetFromJsonAsync<List<LicitacionResponseDto>>("/api/licitaciones?estado=Adjudicada");
        Assert.NotNull(listado);
        var item = Assert.Single(listado);
        Assert.Equal(licitacionId, item.LicitacionId);
        Assert.Equal(cotizacionGanadoraId, item.CotizacionAdjudicadaId);
        Assert.Equal(detalleDespues.FechaAdjudicacion, item.FechaAdjudicacion);
    }

    // ---- 2. Consumidor idempotente de RecepcionConfirmadaEvent ----

    [Fact]
    public async Task Consumidor_ParcialORechazadaNoHabilitaCalificacion_CompletaSiYEsIdempotente()
    {
        var dbOptions = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase($"DB_Consumer_222_{Guid.NewGuid()}")
            .Options;

        Guid ordenId;
        await using (var dbSeed = new LicitacionesDbContext(dbOptions))
        {
            (ordenId, _, _) = SembrarOrden(dbSeed, despachada: true, recibidaCompleta: false);
            await dbSeed.SaveChangesAsync();
        }

        // 1) Recepción parcial (OrdenCompleta = false) -> NO marca RecibidaCompleta
        var eventoParcial = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            OrdenCompraId: ordenId,
            NumeroRemito: "REM-001",
            FechaRecepcion: DateTime.UtcNow,
            Items: new[] { new RecepcionConfirmadaItemEvent(Guid.NewGuid(), 5m, "UN") },
            OrdenCompleta: false);

        await using (var db = new LicitacionesDbContext(dbOptions))
        {
            var consumer = new RecepcionConfirmadaConsumer(db, NullLogger<RecepcionConfirmadaConsumer>.Instance);
            await consumer.Consume(CrearContextoEvento(eventoParcial));
            var oc = await db.OrdenesCompra.FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.False(oc.RecibidaCompleta);
            Assert.Null(oc.FechaRecepcionCompleta);
            Assert.Equal(1, await db.EventosProcesados.CountAsync());
        }

        // 2) Recepción Rechazada (aunque venga con OrdenCompleta = true) -> ignorada, NO marca RecibidaCompleta
        var eventoRechazado = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            OrdenCompraId: ordenId,
            NumeroRemito: "REM-002",
            FechaRecepcion: DateTime.UtcNow,
            Items: new[] { new RecepcionConfirmadaItemEvent(Guid.NewGuid(), 0m, "UN") },
            OrdenCompleta: true,
            Estado: "Rechazada");

        await using (var db = new LicitacionesDbContext(dbOptions))
        {
            var consumer = new RecepcionConfirmadaConsumer(db, NullLogger<RecepcionConfirmadaConsumer>.Instance);
            await consumer.Consume(CrearContextoEvento(eventoRechazado));
            var oc = await db.OrdenesCompra.FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.False(oc.RecibidaCompleta);
            Assert.Null(oc.FechaRecepcionCompleta);
            Assert.Equal(2, await db.EventosProcesados.CountAsync());
        }

        // 3) Recepción Completa (OrdenCompleta = true) -> marca RecibidaCompleta = true
        var fechaCompleta = new DateTime(2026, 11, 21, 15, 30, 0, DateTimeKind.Utc);
        var eventIdCompleta = Guid.NewGuid();
        var eventoCompleto = new RecepcionConfirmadaEvent(
            EventId: eventIdCompleta,
            OccurredAt: fechaCompleta,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            OrdenCompraId: ordenId,
            NumeroRemito: "REM-003",
            FechaRecepcion: fechaCompleta,
            Items: new[] { new RecepcionConfirmadaItemEvent(Guid.NewGuid(), 10m, "UN") },
            OrdenCompleta: true);

        await using (var db = new LicitacionesDbContext(dbOptions))
        {
            var consumer = new RecepcionConfirmadaConsumer(db, NullLogger<RecepcionConfirmadaConsumer>.Instance);
            await consumer.Consume(CrearContextoEvento(eventoCompleto));
            var oc = await db.OrdenesCompra.FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.True(oc.RecibidaCompleta);
            Assert.Equal(fechaCompleta, oc.FechaRecepcionCompleta);
            Assert.Equal(3, await db.EventosProcesados.CountAsync());
        }

        // 4) Reentrega del mismo EventId -> idempotente, no duplica ni pisa fecha
        await using (var db = new LicitacionesDbContext(dbOptions))
        {
            var consumer = new RecepcionConfirmadaConsumer(db, NullLogger<RecepcionConfirmadaConsumer>.Instance);
            await consumer.Consume(CrearContextoEvento(eventoCompleto with { FechaRecepcion = fechaCompleta.AddHours(5) }));
            var oc = await db.OrdenesCompra.FirstAsync(x => x.OrdenCompraId == ordenId);
            Assert.True(oc.RecibidaCompleta);
            Assert.Equal(fechaCompleta, oc.FechaRecepcionCompleta);
            Assert.Equal(3, await db.EventosProcesados.CountAsync());
        }

        // 5) OrdenCompra inexistente -> loguea advertencia sin lanzar NullReferenceException
        var eventoOrdenInexistente = eventoCompleto with { EventId = Guid.NewGuid(), OrdenCompraId = Guid.NewGuid() };
        await using (var db = new LicitacionesDbContext(dbOptions))
        {
            var consumer = new RecepcionConfirmadaConsumer(db, NullLogger<RecepcionConfirmadaConsumer>.Instance);
            await consumer.Consume(CrearContextoEvento(eventoOrdenInexistente));
            Assert.Equal(3, await db.EventosProcesados.CountAsync());
        }
    }

    // ---- 3. Calificación por orden (404, 409, 400, 403, upsert, múltiples órdenes, GET propio) ----

    [Fact]
    public async Task CalificarOrden_NoRecibidaCompleta_Devuelve409_Inexistente404_DatosInvalidos400()
    {
        Guid ordenSoloDespachada = Guid.Empty;
        Guid ordenRecibidaCompleta = Guid.Empty;

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            (ordenSoloDespachada, _, _) = SembrarOrden(db, despachada: true, recibidaCompleta: false);
            (ordenRecibidaCompleta, _, _) = SembrarOrden(db, despachada: true, recibidaCompleta: true);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // Orden inexistente -> 404
        var cid404 = Guid.NewGuid().ToString();
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/ordenes-compra/{Guid.NewGuid()}/calificacion"))
        {
            req.Headers.Add("X-Correlation-ID", cid404);
            req.Content = JsonContent.Create(new UpsertCalificacionOrdenCompraRequestDto(5, "Ok"));
            var resp404 = await client.SendAsync(req);
            await AssertProblemDetailsWithCorrelationIdAsync(resp404, HttpStatusCode.NotFound, cid404);
        }

        // Orden NO recibida completa -> 409 Conflict
        var cid409 = Guid.NewGuid().ToString();
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/ordenes-compra/{ordenSoloDespachada}/calificacion"))
        {
            req.Headers.Add("X-Correlation-ID", cid409);
            req.Content = JsonContent.Create(new UpsertCalificacionOrdenCompraRequestDto(5, "Todavía no llegó"));
            var resp409 = await client.SendAsync(req);
            await AssertProblemDetailsWithCorrelationIdAsync(resp409, HttpStatusCode.Conflict, cid409);
        }

        // Calificación fuera de rango (0 y 6) -> 400
        foreach (var invalida in new[] { 0, 6 })
        {
            var cid400 = Guid.NewGuid().ToString();
            using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/ordenes-compra/{ordenRecibidaCompleta}/calificacion");
            req.Headers.Add("X-Correlation-ID", cid400);
            req.Content = JsonContent.Create(new UpsertCalificacionOrdenCompraRequestDto(invalida, "Inválida"));
            var resp400 = await client.SendAsync(req);
            await AssertProblemDetailsWithCorrelationIdAsync(resp400, HttpStatusCode.BadRequest, cid400);
        }

        // Comentario > 300 caracteres -> 400
        var cidObs = Guid.NewGuid().ToString();
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/ordenes-compra/{ordenRecibidaCompleta}/calificacion"))
        {
            req.Headers.Add("X-Correlation-ID", cidObs);
            req.Content = JsonContent.Create(new UpsertCalificacionOrdenCompraRequestDto(4, new string('X', 301)));
            var respObs = await client.SendAsync(req);
            await AssertProblemDetailsWithCorrelationIdAsync(respObs, HttpStatusCode.BadRequest, cidObs);
        }
    }

    [Theory]
    [InlineData("Proveedor")]
    [InlineData("Capataz")]
    public async Task EndpointsCalificacionPorOrden_RolNoAutorizado_Devuelve403ProblemDetails(string rol)
    {
        Guid ordenId = Guid.Empty;
        using var factory = CreateIsolatedFactory(out _, db =>
        {
            (ordenId, _, _) = SembrarOrden(db, despachada: true, recibidaCompleta: true);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var putResp = await client.PutAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(5, "Intento sin permiso"));
        await AssertProblemDetailsWithCorrelationIdAsync(putResp, HttpStatusCode.Forbidden, cid);

        var getResp = await client.GetAsync($"/api/ordenes-compra/{ordenId}/calificacion");
        await AssertProblemDetailsWithCorrelationIdAsync(getResp, HttpStatusCode.Forbidden, cid);
    }

    [Fact]
    public async Task CalificarVariasOrdenesYEditar_PromediaEnCotizacionesYExponeEnListadoYReputacionDelProveedor()
    {
        var proveedorCalificadoId = Guid.NewGuid();
        var proveedorSinCalifId = Guid.NewGuid();
        var compradorId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        Guid orden1Id = Guid.Empty;
        Guid orden2Id = Guid.Empty;
        var licitacionComparativaId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            (orden1Id, _, _) = SembrarOrden(db, proveedorId: proveedorCalificadoId, despachada: true, recibidaCompleta: true);
            (orden2Id, _, _) = SembrarOrden(db, proveedorId: proveedorCalificadoId, despachada: true, recibidaCompleta: true);

            // Licitación abierta con 2 ofertas para probar promedio, cantidad y scoring en GET /api/cotizaciones
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionComparativaId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-222-COMP",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        LicitacionId = licitacionComparativaId,
                        ArticuloId = articuloId,
                        Descripcion = "Arena gruesa",
                        Cantidad = 10m,
                        UnidadMedida = "M3"
                    }
                }
            });

            var cotiz1 = Guid.NewGuid();
            var cotiz2 = Guid.NewGuid();
            db.Cotizaciones.AddRange(
                new Cotizacion
                {
                    CotizacionId = cotiz1,
                    LicitacionId = licitacionComparativaId,
                    ProveedorId = proveedorCalificadoId,
                    Flete = 100m,
                    PlazoEntregaDias = 3,
                    FechaCreacion = DateTime.UtcNow.AddMinutes(-20),
                    Items = new List<CotizacionItem>
                    {
                        new() { Id = Guid.NewGuid(), CotizacionId = cotiz1, ArticuloId = articuloId, PrecioUnitario = 1000m }
                    }
                },
                new Cotizacion
                {
                    CotizacionId = cotiz2,
                    LicitacionId = licitacionComparativaId,
                    ProveedorId = proveedorSinCalifId,
                    Flete = 100m,
                    PlazoEntregaDias = 3,
                    FechaCreacion = DateTime.UtcNow.AddMinutes(-10),
                    Items = new List<CotizacionItem>
                    {
                        new() { Id = Guid.NewGuid(), CotizacionId = cotiz2, ArticuloId = articuloId, PrecioUnitario = 1000m }
                    }
                });
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", compradorId));

        var comprador2Id = Guid.NewGuid();
        var clientComprador2 = factory.CreateClient();
        clientComprador2.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador", comprador2Id));

        var clientAdmin = factory.CreateClient();
        clientAdmin.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin", adminId));

        var clientProveedor = factory.CreateClient();
        clientProveedor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorCalificadoId));

        // Proveedor sin calificaciones aún -> mi-reputacion devuelve 200 OK con vacíos
        var repVacia = await clientProveedor.GetFromJsonAsync<ReputacionProveedorResponseDto>(
            "/api/cotizaciones/proveedores/mi-reputacion");
        Assert.NotNull(repVacia);
        Assert.Equal(proveedorCalificadoId, repVacia.ProveedorId);
        Assert.Null(repVacia.Calificacion);
        Assert.Equal(0, repVacia.CantidadCalificaciones);
        Assert.Null(repVacia.FechaActualizacion);
        Assert.Empty(repVacia.UltimosComentarios);

        // 1) Comprador califica orden 1 con 3 y luego la edita a 5 (no duplica fila)
        var r1 = await clientComprador.PutAsJsonAsync(
            $"/api/ordenes-compra/{orden1Id}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(3, "Llegó con demora"));
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);

        var r1Edit = await clientComprador.PutAsJsonAsync(
            $"/api/ordenes-compra/{orden1Id}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(5, "  Excelente atención final  "));
        Assert.Equal(HttpStatusCode.OK, r1Edit.StatusCode);
        var dtoOrden1 = await r1Edit.Content.ReadFromJsonAsync<CalificacionOrdenCompraResponseDto>();
        Assert.NotNull(dtoOrden1);
        Assert.Equal(5, dtoOrden1.Calificacion);
        Assert.Equal("Excelente atención final", dtoOrden1.Comentario);
        Assert.Equal(5m, dtoOrden1.PromedioProveedor);
        Assert.Equal(1, dtoOrden1.CantidadCalificacionesProveedor);

        // 2) Mismo comprador califica orden 2 del mismo proveedor con 4 y sin comentario
        var r2 = await clientComprador.PutAsJsonAsync(
            $"/api/licitaciones/ordenes-compra/{orden2Id}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(4, "   "));
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var dtoOrden2 = await r2.Content.ReadFromJsonAsync<CalificacionOrdenCompraResponseDto>();
        Assert.NotNull(dtoOrden2);
        Assert.Null(dtoOrden2.Comentario);
        Assert.Equal(4.5m, dtoOrden2.PromedioProveedor);
        Assert.Equal(2, dtoOrden2.CantidadCalificacionesProveedor);

        // 3) Admin no puede calificar (#232 -> 403), pero un segundo Comprador califica orden 1 con 5 y comentario -> suma 3.ª calificación (promedio (5+4+5)/3 = 4.67)
        var rAdminRechazado = await clientAdmin.PutAsJsonAsync(
            $"/api/ordenes-compra/{orden1Id}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(5, "Intento de admin"));
        Assert.Equal(HttpStatusCode.Forbidden, rAdminRechazado.StatusCode);

        var r3 = await clientComprador2.PutAsJsonAsync(
            $"/api/ordenes-compra/{orden1Id}/calificacion",
            new UpsertCalificacionOrdenCompraRequestDto(5, "Entrega impecable en obra"));
        Assert.Equal(HttpStatusCode.OK, r3.StatusCode);
        var dtoComprador2 = await r3.Content.ReadFromJsonAsync<CalificacionOrdenCompraResponseDto>();
        Assert.NotNull(dtoComprador2);
        Assert.Equal(4.67m, dtoComprador2.PromedioProveedor);
        Assert.Equal(3, dtoComprador2.CantidadCalificacionesProveedor);

        // 4) GET /api/ordenes-compra/{id}/calificacion devuelve la calificación del usuario autenticado
        var miCalifComprador = await clientComprador.GetFromJsonAsync<CalificacionOrdenCompraResponseDto>(
            $"/api/ordenes-compra/{orden1Id}/calificacion");
        Assert.NotNull(miCalifComprador);
        Assert.Equal(compradorId, miCalifComprador.UsuarioId);
        Assert.Equal("Excelente atención final", miCalifComprador.Comentario);

        // Admin en orden 2 todavía no calificó -> 404
        var adminEnOrden2 = await clientAdmin.GetAsync($"/api/ordenes-compra/{orden2Id}/calificacion");
        Assert.Equal(HttpStatusCode.NotFound, adminEnOrden2.StatusCode);

        // 5) GET /api/ordenes-compra incluye recibidaCompleta y miCalificacion / miComentarioCalificacion
        var listadoOrdenes = await clientComprador.GetFromJsonAsync<List<OrdenCompraResponseDto>>("/api/ordenes-compra");
        Assert.NotNull(listadoOrdenes);
        var itemOrden1 = listadoOrdenes.Single(o => o.Id == orden1Id);
        Assert.True(itemOrden1.RecibidaCompleta);
        Assert.NotNull(itemOrden1.FechaRecepcionCompleta);
        Assert.Equal(5, itemOrden1.MiCalificacion);
        Assert.Equal("Excelente atención final", itemOrden1.MiComentarioCalificacion);
        Assert.NotNull(itemOrden1.FechaMiCalificacion);

        // 6) GET /api/cotizaciones devuelve promedio (4.67), cantidadCalificacionesProveedor (3) y alimenta el scoring
        var cotizaciones = await clientComprador.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacionComparativaId}");
        Assert.NotNull(cotizaciones);
        var ofertaCalif = cotizaciones.Single(c => c.ProveedorId == proveedorCalificadoId);
        var ofertaSinCalif = cotizaciones.Single(c => c.ProveedorId == proveedorSinCalifId);

        Assert.Equal(4.67m, ofertaCalif.CalificacionProveedor);
        Assert.Equal(3, ofertaCalif.CantidadCalificacionesProveedor);
        Assert.True(ofertaCalif.EsGanadora);
        Assert.True(ofertaCalif.PuntajeTotal > ofertaSinCalif.PuntajeTotal);

        Assert.Null(ofertaSinCalif.CalificacionProveedor);
        Assert.Equal(0, ofertaSinCalif.CantidadCalificacionesProveedor);
        Assert.Equal(0m, ofertaSinCalif.DesgloseScoring!.PuntajeCalificacion);

        // 7) Reputación del proveedor: devuelve promedio, cantidad y solo los 2 comentarios no vacíos (más reciente primero)
        var reputacion = await clientProveedor.GetFromJsonAsync<ReputacionProveedorResponseDto>(
            "/api/cotizaciones/proveedores/mi-reputacion");
        Assert.NotNull(reputacion);
        Assert.Equal(4.67m, reputacion.Calificacion);
        Assert.Equal(3, reputacion.CantidadCalificaciones);
        Assert.Equal(2, reputacion.UltimosComentarios.Count);
        Assert.Equal("Entrega impecable en obra", reputacion.UltimosComentarios[0].Comentario);
        Assert.Equal("Excelente atención final", reputacion.UltimosComentarios[1].Comentario);

        // Alias /api/ordenes-compra/mi-reputacion devuelve lo mismo
        var reputacionAlias = await clientProveedor.GetFromJsonAsync<ReputacionProveedorResponseDto>(
            "/api/ordenes-compra/mi-reputacion");
        Assert.NotNull(reputacionAlias);
        Assert.Equal(4.67m, reputacionAlias.Calificacion);
        Assert.Equal(2, reputacionAlias.UltimosComentarios.Count);

        // GET /api/cotizaciones/proveedores/{id}/calificacion también incluye ultimosComentarios
        var califConComentarios = await clientProveedor.GetFromJsonAsync<CalificacionProveedorResponseDto>(
            $"/api/cotizaciones/proveedores/{proveedorCalificadoId}/calificacion");
        Assert.NotNull(califConComentarios);
        Assert.NotNull(califConComentarios.UltimosComentarios);
        Assert.Equal(2, califConComentarios.UltimosComentarios!.Count);
    }

    [Fact]
    public async Task MiReputacion_OtroProveedorORolDistintoAProveedor_Devuelve403ProblemDetails()
    {
        using var factory = CreateIsolatedFactory(out _);
        var provA = Guid.NewGuid();
        var provB = Guid.NewGuid();

        // Proveedor A intenta pasar proveedorId de B por query en mi-reputacion -> 403
        var clientProvA = factory.CreateClient();
        var cid1 = Guid.NewGuid().ToString();
        clientProvA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provA));
        clientProvA.DefaultRequestHeaders.Add("X-Correlation-ID", cid1);

        var respQueryAjeno = await clientProvA.GetAsync($"/api/cotizaciones/proveedores/mi-reputacion?proveedorId={provB}");
        await AssertProblemDetailsWithCorrelationIdAsync(respQueryAjeno, HttpStatusCode.Forbidden, cid1);

        var respAliasAjeno = await clientProvA.GetAsync($"/api/ordenes-compra/mi-reputacion?proveedorId={provB}");
        await AssertProblemDetailsWithCorrelationIdAsync(respAliasAjeno, HttpStatusCode.Forbidden, cid1);

        // Proveedor A intenta consultar /{provB}/calificacion -> 403
        var respOtroId = await clientProvA.GetAsync($"/api/cotizaciones/proveedores/{provB}/calificacion");
        await AssertProblemDetailsWithCorrelationIdAsync(respOtroId, HttpStatusCode.Forbidden, cid1);

        // Roles distintos de Proveedor (Comprador, Admin, Capataz) en mi-reputacion -> 403
        foreach (var rol in new[] { "Comprador", "Admin", "Capataz" })
        {
            var clientRol = factory.CreateClient();
            var cidRol = Guid.NewGuid().ToString();
            clientRol.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));
            clientRol.DefaultRequestHeaders.Add("X-Correlation-ID", cidRol);

            var resp1 = await clientRol.GetAsync("/api/cotizaciones/proveedores/mi-reputacion");
            await AssertProblemDetailsWithCorrelationIdAsync(resp1, HttpStatusCode.Forbidden, cidRol);

            var resp2 = await clientRol.GetAsync("/api/ordenes-compra/mi-reputacion");
            await AssertProblemDetailsWithCorrelationIdAsync(resp2, HttpStatusCode.Forbidden, cidRol);
        }
    }
}
