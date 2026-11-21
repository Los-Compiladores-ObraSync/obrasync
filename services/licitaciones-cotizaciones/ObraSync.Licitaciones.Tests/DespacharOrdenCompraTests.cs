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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class DespacharOrdenCompraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public DespacharOrdenCompraTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out IPublishEndpoint publishSpy,
        Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Despachar_{Guid.NewGuid()}";
        var spy = Substitute.For<IPublishEndpoint>();
        publishSpy = spy;

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
                    options.UseInMemoryDatabase(dbName);
                });

                services.RemoveAll<IPublishEndpoint>();
                services.AddSingleton(spy);

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

    private static (Guid ordenId, Guid obraId, Guid proveedorId) SembrarOrdenEmitida(LicitacionesDbContext db)
    {
        var licId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = obraId,
            Estado = EstadoLicitacion.Adjudicada,
            FechaCreacion = DateTime.UtcNow.AddDays(-1),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(1)
        });
        db.OrdenesCompra.Add(new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = licId,
            CotizacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow
        });

        return (ordenId, obraId, proveedorId);
    }

    // ---- 1. Pruebas unitarias de Dominio y Handler (xUnit + NSubstitute) ----

    [Fact]
    public void Dominio_FechaEstimadaUnspecified_NormalizaAUtcSinCorrimientoLocal()
    {
        var proveedorId = Guid.NewGuid();
        var ahoraUtc = new DateTime(2026, 11, 21, 18, 0, 0, DateTimeKind.Utc);
        var fechaHoyUnspecified = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Unspecified);

        var orden = new OrdenCompra
        {
            OrdenCompraId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Estado = "Emitida"
        };

        var cambio = orden.Despachar(proveedorId, ahoraUtc, fechaHoyUnspecified, "  Entrega en portería  ");

        Assert.True(cambio);
        Assert.Equal(DateTimeKind.Utc, orden.FechaEstimadaLlegada!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc), orden.FechaEstimadaLlegada.Value);
        Assert.Equal("Entrega en portería", orden.Observacion);
        Assert.Equal(proveedorId, orden.DespachadaPor);
        Assert.Equal(ahoraUtc, orden.DespachadaEn);
    }

    [Fact]
    public async Task Handler_DespacharOrdenEmitidaPorProveedorDueno_ActualizaEstadoYPublicaOrdenCompraDespachadaEventUnaSolaVez()
    {
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var ordenId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var fechaEstimada = DateTime.UtcNow.Date.AddDays(3);
        const string observacion = "Sale camión patente AB123CD por la mañana";

        var orden = new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = licitacionId,
            CotizacionId = cotizacionId,
            ProveedorId = proveedorId,
            ObraId = obraId,
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddHours(-2)
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);

        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);
        var antesUtc = DateTime.UtcNow.AddSeconds(-2);

        var resultado = await handler.Handle(
            new DespacharOrdenCompraCommand(ordenId, proveedorId, correlationId, fechaEstimada, $"  {observacion}  "),
            CancellationToken.None);

        Assert.Equal("Despachada", orden.Estado);
        Assert.NotNull(orden.FechaDespacho);
        Assert.InRange(orden.FechaDespacho!.Value, antesUtc, DateTime.UtcNow.AddSeconds(2));
        Assert.Equal(proveedorId, orden.DespachadaPor);
        Assert.Equal(orden.FechaDespacho, orden.DespachadaEn);
        Assert.Equal(fechaEstimada, orden.FechaEstimadaLlegada);
        Assert.Equal(observacion, orden.Observacion);

        Assert.Equal(ordenId, resultado.Id);
        Assert.Equal("Despachada", resultado.Estado);
        Assert.True(resultado.FueDespachada);
        Assert.Equal(orden.FechaDespacho, resultado.FechaDespacho);
        Assert.Equal(orden.FechaDespacho, resultado.DespachadaEn);
        Assert.Equal(proveedorId, resultado.DespachadaPor);
        Assert.Equal(fechaEstimada, resultado.FechaEstimadaLlegada);
        Assert.Equal(observacion, resultado.Observacion);
        Assert.Equal(obraId, resultado.ObraId);

        Received.InOrder(() =>
        {
            ordenRepo.UpdateAsync(orden, Arg.Any<CancellationToken>());
            publishEndpoint.Publish<OrdenCompraDespachadaEvent>(
                Arg.Any<OrdenCompraDespachadaEvent>(),
                Arg.Any<CancellationToken>());
            unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });

        await publishEndpoint.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Is<OrdenCompraDespachadaEvent>(e =>
                e.EventId != Guid.Empty &&
                e.OccurredAt >= antesUtc &&
                e.CorrelationId == correlationId &&
                e.OrdenCompraId == ordenId &&
                e.ObraId == obraId &&
                e.FechaDespacho == orden.FechaDespacho.Value &&
                e.FechaEstimadaLlegada == fechaEstimada &&
                e.ObservacionDespacho == observacion),
            Arg.Any<CancellationToken>());

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_OtroProveedor_LanzaUnauthorizedAccessExceptionYNoPublicaEvento()
    {
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var ordenId = Guid.NewGuid();
        var proveedorDueno = Guid.NewGuid();
        var otroProveedor = Guid.NewGuid();
        var orden = new OrdenCompra
        {
            OrdenCompraId = ordenId,
            ProveedorId = proveedorDueno,
            ObraId = Guid.NewGuid(),
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddHours(-1)
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);
        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new DespacharOrdenCompraCommand(ordenId, otroProveedor, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("Emitida", orden.Estado);
        await ordenRepo.DidNotReceive().UpdateAsync(Arg.Any<OrdenCompra>(), Arg.Any<CancellationToken>());
        await publishEndpoint.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_OrdenInexistente_LanzaKeyNotFoundExceptionYNoPublicaEvento()
    {
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var idInexistente = Guid.NewGuid();
        ordenRepo.GetByIdAsync(idInexistente, Arg.Any<CancellationToken>()).Returns((OrdenCompra?)null);

        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            handler.Handle(new DespacharOrdenCompraCommand(idInexistente, Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));

        await publishEndpoint.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_OrdenYaDespachada_EsIdempotenteNoPisaDatosYNoVuelveAPublicarEvento()
    {
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var ordenId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var fechaOriginal = DateTime.UtcNow.AddHours(-3);
        var fechaEstimadaOriginal = DateTime.UtcNow.Date.AddDays(2);
        const string observacionOriginal = "Observación original";

        var orden = new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = Guid.NewGuid(),
            CotizacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            ObraId = Guid.NewGuid(),
            Estado = "Despachada",
            FechaEmision = DateTime.UtcNow.AddDays(-1),
            FechaDespacho = fechaOriginal,
            DespachadaPor = proveedorId,
            FechaEstimadaLlegada = fechaEstimadaOriginal,
            Observacion = observacionOriginal
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);

        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);

        // Incluso enviando una fecha estimada que hoy ya sería pasada u otra observación, devuelve el estado guardado
        var resultado = await handler.Handle(
            new DespacharOrdenCompraCommand(
                ordenId,
                proveedorId,
                Guid.NewGuid(),
                FechaEstimadaLlegada: DateTime.UtcNow.Date.AddDays(-5),
                Observacion: "Intento de pisar observación"),
            CancellationToken.None);

        Assert.Equal("Despachada", resultado.Estado);
        Assert.Equal(fechaOriginal, resultado.FechaDespacho);
        Assert.Equal(fechaOriginal, resultado.DespachadaEn);
        Assert.Equal(proveedorId, resultado.DespachadaPor);
        Assert.Equal(fechaEstimadaOriginal, resultado.FechaEstimadaLlegada);
        Assert.Equal(observacionOriginal, resultado.Observacion);

        await ordenRepo.DidNotReceive().UpdateAsync(Arg.Any<OrdenCompra>(), Arg.Any<CancellationToken>());
        await publishEndpoint.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_SiFallaActualizacionEnRepositorio_NoPublicaEvento()
    {
        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var ordenId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var orden = new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = Guid.NewGuid(),
            CotizacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            ObraId = Guid.NewGuid(),
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddHours(-1)
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);
        ordenRepo.UpdateAsync(orden, Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Error simulado al actualizar OrdenCompra"));

        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.Handle(new DespacharOrdenCompraCommand(ordenId, proveedorId, Guid.NewGuid()), CancellationToken.None));

        await publishEndpoint.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_SiFallaSaveChangesAsync_PropagaExcepcionYAbortaOutbox()
    {
        var dbName = $"DB_Outbox_Rollback_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        await using var db = new LicitacionesDbContext(options);

        var ordenRepo = Substitute.For<IOrdenCompraRepository>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var unitOfWork = Substitute.For<IUnitOfWork>();

        var ordenId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var orden = new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = Guid.NewGuid(),
            CotizacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            ObraId = Guid.NewGuid(),
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddHours(-1)
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Fallo simulado de commit en BD"));

        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.Handle(new DespacharOrdenCompraCommand(ordenId, proveedorId, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(0, await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
    }

    // ---- 2. Pruebas de integración HTTP con WebApplicationFactory ----

    [Fact]
    public async Task FlujoCompleto_AdjudicarLuegoDespacharPorProveedorDueno_PublicaEventoEsIdempotenteYComprasVeSoloLectura()
    {
        var licitacionId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0901",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-3),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(4),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Hierro del 10",
                        Cantidad = 20m,
                        UnidadMedida = "BARRA"
                    }
                }
            });

            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizacionId,
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                Flete = 500m,
                PlazoEntregaDias = 2,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotizacionId,
                        ArticuloId = articuloId,
                        PrecioUnitario = 1500m
                    }
                }
            });
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // 1. Adjudicar la cotización -> crea OrdenCompra en estado "Emitida"
        var adjudicarResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/adjudicar",
            new { cotizacionId });
        Assert.Equal(HttpStatusCode.OK, adjudicarResp.StatusCode);

        var ocEmitida = await adjudicarResp.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(ocEmitida);
        Assert.Equal("Emitida", ocEmitida.Estado);
        Assert.False(ocEmitida.FueDespachada);
        Assert.Null(ocEmitida.FechaDespacho);
        Assert.Null(ocEmitida.DespachadaPor);
        Assert.Null(ocEmitida.DespachadaEn);

        // 2. Compras lista en GET /api/ordenes-compra antes del despacho (solo lectura)
        var listarAntesResp = await clientComprador.GetAsync("/api/ordenes-compra");
        Assert.Equal(HttpStatusCode.OK, listarAntesResp.StatusCode);
        Assert.Equal("1", listarAntesResp.Headers.GetValues("X-Total-Count").Single());

        // 3. Proveedor dueño marca el despacho con fechaEstimadaLlegada y observacion propagando X-Correlation-ID
        var correlationId = Guid.NewGuid();
        var fechaEstimada = DateTime.UtcNow.Date.AddDays(4);
        const string observacion = "Remito #4421 - descargar en portón norte";

        var clientProveedor = factory.CreateClient();
        clientProveedor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        clientProveedor.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId.ToString());

        var despacharResp = await clientProveedor.PostAsJsonAsync(
            $"/api/ordenes-compra/{ocEmitida.Id}/despachar",
            new DespacharOrdenCompraRequestDto(fechaEstimada, observacion));
        Assert.Equal(HttpStatusCode.OK, despacharResp.StatusCode);

        var despacharJson = await despacharResp.Content.ReadAsStringAsync();
        using var docDespacho = JsonDocument.Parse(despacharJson);
        Assert.EndsWith("Z", docDespacho.RootElement.GetProperty("fechaDespacho").GetString());
        Assert.EndsWith("Z", docDespacho.RootElement.GetProperty("despachadaEn").GetString());

        var ocDespachada = docDespacho.RootElement.Deserialize<OrdenCompraResponseDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(ocDespachada);
        Assert.Equal("Despachada", ocDespachada.Estado);
        Assert.True(ocDespachada.FueDespachada);
        Assert.NotNull(ocDespachada.FechaDespacho);
        Assert.Equal(ocDespachada.FechaDespacho, ocDespachada.DespachadaEn);
        Assert.Equal(proveedorId, ocDespachada.DespachadaPor);
        Assert.Equal(fechaEstimada, ocDespachada.FechaEstimadaLlegada);
        Assert.Equal(observacion, ocDespachada.Observacion);

        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Is<OrdenCompraDespachadaEvent>(e =>
                e.EventId != Guid.Empty &&
                e.CorrelationId == correlationId &&
                e.OrdenCompraId == ocEmitida.Id &&
                e.ObraId == obraId &&
                e.FechaDespacho == ocDespachada.FechaDespacho!.Value &&
                e.FechaEstimadaLlegada == fechaEstimada &&
                e.ObservacionDespacho == observacion),
            Arg.Any<CancellationToken>());

        // 4. Comprador ve los datos del despacho en GET /api/ordenes-compra
        var listaDespues = await clientComprador.GetFromJsonAsync<List<OrdenCompraResponseDto>>("/api/ordenes-compra?estado=Despachada");
        Assert.NotNull(listaDespues);
        var ocListadoDespues = Assert.Single(listaDespues);
        Assert.Equal("Despachada", ocListadoDespues.Estado);
        Assert.True(ocListadoDespues.FueDespachada);
        Assert.Equal(proveedorId, ocListadoDespues.DespachadaPor);
        Assert.Equal(ocDespachada.FechaDespacho, ocListadoDespues.DespachadaEn);
        Assert.Equal(fechaEstimada, ocListadoDespues.FechaEstimadaLlegada);
        Assert.Equal(observacion, ocListadoDespues.Observacion);

        // 5. Segundo intento de despacho del mismo Proveedor dueño devuelve 200 OK idempotente,
        //    conserva los datos originales y NO vuelve a publicar el evento.
        var segundoResp = await clientProveedor.PostAsJsonAsync(
            $"/api/ordenes-compra/{ocEmitida.Id}/despachar",
            new DespacharOrdenCompraRequestDto(DateTime.UtcNow.Date.AddDays(10), "Intento de sobrescritura"));
        Assert.Equal(HttpStatusCode.OK, segundoResp.StatusCode);

        var ocSegundoIntento = await segundoResp.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(ocSegundoIntento);
        Assert.Equal(ocDespachada.FechaDespacho, ocSegundoIntento.FechaDespacho);
        Assert.Equal(ocDespachada.DespachadaEn, ocSegundoIntento.DespachadaEn);
        Assert.Equal(proveedorId, ocSegundoIntento.DespachadaPor);
        Assert.Equal(fechaEstimada, ocSegundoIntento.FechaEstimadaLlegada);
        Assert.Equal(observacion, ocSegundoIntento.Observacion);

        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_SinBody_Devuelve200OkYPublicaEventoConOpcionalesNulos()
    {
        Guid ordenId = Guid.Empty;
        Guid obraId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, obraId, proveedorId) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // Llamada real sin requestBody (HttpContent null)
        var resp = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("Despachada", dto.Estado);
        Assert.True(dto.FueDespachada);
        Assert.Equal(proveedorId, dto.DespachadaPor);
        Assert.NotNull(dto.DespachadaEn);
        Assert.Null(dto.FechaEstimadaLlegada);
        Assert.Null(dto.Observacion);

        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Is<OrdenCompraDespachadaEvent>(e =>
                e.OrdenCompraId == ordenId &&
                e.ObraId == obraId &&
                e.FechaEstimadaLlegada == null &&
                e.ObservacionDespacho == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_OtroProveedorNoDueno_Retorna403ProblemDetailsYNoPublicaEvento()
    {
        Guid ordenId = Guid.Empty;
        Guid proveedorDueno = Guid.Empty;
        var otroProveedor = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, _, proveedorDueno) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", otroProveedor));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(DateTime.UtcNow.Date.AddDays(1), "No soy el dueño"));

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Forbidden, cid);
        await publishSpy.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    [InlineData("Capataz")]
    public async Task Despachar_ConRolDistintoAProveedor_Retorna403ProblemDetailsYNoPublicaEvento(string rol)
    {
        Guid ordenId = Guid.Empty;
        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, _, _) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Forbidden, cid);
        await publishSpy.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_FechaEstimadaAnteriorAHoy_Retorna400ProblemDetailsYNoPublicaEvento()
    {
        Guid ordenId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, _, proveedorId) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var ayer = DateTime.UtcNow.Date.AddDays(-1);
        var resp = await client.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(ayer, "Fecha pasada"));

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.BadRequest, cid);
        await publishSpy.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_ObservacionSupera200Caracteres_Retorna400ProblemDetailsYNoPublicaEvento()
    {
        Guid ordenId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, _, proveedorId) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var observacionLarga = new string('A', 201);
        var resp = await client.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(DateTime.UtcNow.Date.AddDays(1), observacionLarga));

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.BadRequest, cid);
        await publishSpy.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_OrdenInexistente_Retorna404ProblemDetailsConCorrelationId()
    {
        using var factory = CreateIsolatedFactory(out var publishSpy);
        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsync($"/api/ordenes-compra/{Guid.NewGuid()}/despachar", null);

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.NotFound, cid);
        await publishSpy.DidNotReceive().Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Despachar_SinToken_Retorna401ProblemDetails()
    {
        using var factory = CreateIsolatedFactory(out _);
        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsync($"/api/ordenes-compra/{Guid.NewGuid()}/despachar", null);

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Unauthorized, cid);
    }

    [Fact]
    public async Task Despachar_PorProveedorDuenoYRutaBajoLicitaciones_FuncionaIgual()
    {
        Guid ordenId = Guid.Empty;
        Guid obraId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, db =>
        {
            (ordenId, obraId, proveedorId) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(null, "Ruta alternativa bajo licitaciones"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("Despachada", dto.Estado);
        Assert.True(dto.FueDespachada);
        Assert.Equal(obraId, dto.ObraId);
        Assert.Equal(proveedorId, dto.DespachadaPor);
        Assert.Equal("Ruta alternativa bajo licitaciones", dto.Observacion);

        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Is<OrdenCompraDespachadaEvent>(e =>
                e.OrdenCompraId == ordenId &&
                e.ObraId == obraId &&
                e.ObservacionDespacho == "Ruta alternativa bajo licitaciones"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListarOrdenesCompra_ProveedorSoloVeSusOrdenesYCapatazRecibe403_PaginacionValida400()
    {
        var proveedorA = Guid.NewGuid();
        var proveedorB = Guid.NewGuid();
        var licA = Guid.NewGuid();
        var licB = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(out _, db =>
        {
            db.Licitaciones.AddRange(
                new Licitacion
                {
                    LicitacionId = licA,
                    RequisicionId = Guid.NewGuid(),
                    NumeroRequisicion = "REQ-2026-0910",
                    ObraId = Guid.NewGuid(),
                    Estado = EstadoLicitacion.Adjudicada,
                    FechaCreacion = DateTime.UtcNow.AddDays(-2),
                    FechaLimiteCotizacion = DateTime.UtcNow.AddDays(2)
                },
                new Licitacion
                {
                    LicitacionId = licB,
                    RequisicionId = Guid.NewGuid(),
                    NumeroRequisicion = "REQ-2026-0911",
                    ObraId = Guid.NewGuid(),
                    Estado = EstadoLicitacion.Adjudicada,
                    FechaCreacion = DateTime.UtcNow.AddDays(-1),
                    FechaLimiteCotizacion = DateTime.UtcNow.AddDays(2)
                });

            db.OrdenesCompra.AddRange(
                new OrdenCompra
                {
                    OrdenCompraId = Guid.NewGuid(),
                    LicitacionId = licA,
                    CotizacionId = Guid.NewGuid(),
                    ProveedorId = proveedorA,
                    Estado = "Emitida",
                    FechaEmision = DateTime.UtcNow.AddHours(-2)
                },
                new OrdenCompra
                {
                    OrdenCompraId = Guid.NewGuid(),
                    LicitacionId = licB,
                    CotizacionId = Guid.NewGuid(),
                    ProveedorId = proveedorB,
                    Estado = "Despachada",
                    FechaEmision = DateTime.UtcNow.AddHours(-1),
                    FechaDespacho = DateTime.UtcNow,
                    DespachadaPor = proveedorB,
                    Observacion = "Despachada por proveedor B"
                });
        });

        // Proveedor A consulta pasando proveedorB en query: igual solo ve la suya
        var clientProvA = factory.CreateClient();
        clientProvA.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var respProvA = await clientProvA.GetAsync($"/api/ordenes-compra?proveedorId={proveedorB}");
        Assert.Equal(HttpStatusCode.OK, respProvA.StatusCode);
        Assert.Equal("1", respProvA.Headers.GetValues("X-Total-Count").Single());
        var ordenesProvA = await respProvA.Content.ReadFromJsonAsync<List<OrdenCompraResponseDto>>();
        Assert.NotNull(ordenesProvA);
        var unica = Assert.Single(ordenesProvA);
        Assert.Equal(proveedorA, unica.ProveedorId);

        // Capataz -> 403
        var clientCapataz = factory.CreateClient();
        var cidCapataz = Guid.NewGuid().ToString();
        clientCapataz.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        clientCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", cidCapataz);
        var respCapataz = await clientCapataz.GetAsync("/api/ordenes-compra");
        await AssertProblemDetailsWithCorrelationIdAsync(respCapataz, HttpStatusCode.Forbidden, cidCapataz);

        // Paginación inválida -> 400
        var clientComprador = factory.CreateClient();
        var cidPag = Guid.NewGuid().ToString();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        clientComprador.DefaultRequestHeaders.Add("X-Correlation-ID", cidPag);
        var respPagInvalida = await clientComprador.GetAsync("/api/ordenes-compra?pagina=0&tamano=10");
        await AssertProblemDetailsWithCorrelationIdAsync(respPagInvalida, HttpStatusCode.BadRequest, cidPag);
    }
}
