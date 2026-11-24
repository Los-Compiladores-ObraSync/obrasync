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
using Microsoft.Extensions.Logging;
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
using ObraSync.Licitaciones.Infrastructure.Consumers;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Tarjeta #248: Re-despacho del remanente de una Orden de Compra con recepción parcial.
/// </summary>
public class RedespachoRemanenteOrdenCompraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public RedespachoRemanenteOrdenCompraTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        out IPublishEndpoint publishSpy,
        out IChatOrdenNotifier notifierSpy,
        Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Redespacho_{Guid.NewGuid()}";
        var pSpy = Substitute.For<IPublishEndpoint>();
        var nSpy = Substitute.For<IChatOrdenNotifier>();
        publishSpy = pSpy;
        notifierSpy = nSpy;

        var factory = _baseFactory.WithWebHostBuilder(builder =>
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
                services.AddSingleton(pSpy);

                services.RemoveAll<IChatOrdenNotifier>();
                services.AddSingleton(nSpy);

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

        _ = factory.Server;
        return factory;
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
            FechaCreacion = DateTime.UtcNow.AddDays(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(2)
        });
        db.OrdenesCompra.Add(new OrdenCompra
        {
            OrdenCompraId = ordenId,
            LicitacionId = licId,
            CotizacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Estado = "Emitida",
            FechaEmision = DateTime.UtcNow.AddDays(-1)
        });

        return (ordenId, obraId, proveedorId);
    }

    private static async Task SimularRecepcionAsync(
        IServiceProvider serviceProvider,
        Guid ordenId,
        Guid obraId,
        bool ordenCompleta,
        string estado = "Confirmada")
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<RecepcionConfirmadaConsumer>>();
        var chatRepo = scope.ServiceProvider.GetService<IChatOrdenRepository>();
        var chatNotifier = scope.ServiceProvider.GetService<IChatOrdenNotifier>();
        var consumer = new RecepcionConfirmadaConsumer(db, logger, chatRepo, chatNotifier);

        var consumeCtx = Substitute.For<ConsumeContext<RecepcionConfirmadaEvent>>();
        consumeCtx.CancellationToken.Returns(CancellationToken.None);
        consumeCtx.Message.Returns(new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: obraId,
            OrdenCompraId: ordenId,
            NumeroRemito: $"REM-{Random.Shared.Next(1000, 9999)}",
            FechaRecepcion: DateTime.UtcNow,
            Items: Array.Empty<RecepcionConfirmadaItemEvent>(),
            OrdenCompleta: ordenCompleta,
            Estado: estado));

        await consumer.Consume(consumeCtx);
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

    [Fact]
    public async Task RedespachoExitosoTrasRecepcionParcial_IncrementaNumeroDespacho_PublicaEventoYNotificaChatYCampanita()
    {
        Guid ordenId = Guid.Empty;
        Guid obraId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, out var notifierSpy, db =>
        {
            (ordenId, obraId, proveedorId) = SembrarOrdenEmitida(db);
        });

        var clientProveedor = factory.CreateClient();
        clientProveedor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // 1. Primer despacho -> NumeroDespacho = 1, EsRedespacho = false
        var resp1 = await clientProveedor.PostAsJsonAsync(
            $"/api/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(DateTime.UtcNow.Date.AddDays(2), "Primer envío (80 bolsas)"));
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var dto1 = await resp1.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(dto1);
        Assert.Equal(1, dto1.NumeroDespacho);
        Assert.False(dto1.PuedeRedespachar);

        // 2. Recepción parcial -> habilita re-despacho
        await SimularRecepcionAsync(factory.Services, ordenId, obraId, ordenCompleta: false);

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var listadoParcial = await clientComprador.GetFromJsonAsync<List<OrdenCompraResponseDto>>("/api/ordenes-compra");
        Assert.NotNull(listadoParcial);
        var ocParcial = Assert.Single(listadoParcial);
        Assert.True(ocParcial.TieneRecepcionParcialPendiente);
        Assert.True(ocParcial.PuedeRedespachar);
        Assert.NotNull(ocParcial.FechaUltimaRecepcionParcial);

        // 3. Re-despacho del remanente propagando X-Correlation-ID (probado por ruta alias)
        var cidRedespacho = Guid.NewGuid();
        var fechaEstimada2 = DateTime.UtcNow.Date.AddDays(3);
        const string obsRedespacho = "Sale remanente de 20 bolsas";
        clientProveedor.DefaultRequestHeaders.Add("X-Correlation-ID", cidRedespacho.ToString());

        var resp2 = await clientProveedor.PostAsJsonAsync(
            $"/api/licitaciones/ordenes-compra/{ordenId}/despachar",
            new DespacharOrdenCompraRequestDto(fechaEstimada2, obsRedespacho));
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        var dto2 = await resp2.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.NotNull(dto2);
        Assert.Equal("Despachada", dto2.Estado);
        Assert.Equal(2, dto2.NumeroDespacho);
        Assert.False(dto2.TieneRecepcionParcialPendiente);
        Assert.False(dto2.PuedeRedespachar);
        Assert.Equal(fechaEstimada2, dto2.FechaEstimadaLlegada);
        Assert.Equal(obsRedespacho, dto2.Observacion);

        // Verifica publicación de OrdenCompraDespachadaEvent (#2) con EsRedespacho = true
        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Is<OrdenCompraDespachadaEvent>(e =>
                e.EventId != Guid.Empty &&
                e.CorrelationId == cidRedespacho &&
                e.OrdenCompraId == ordenId &&
                e.ObraId == obraId &&
                e.EsRedespacho == true &&
                e.NumeroDespacho == 2 &&
                e.ObservacionDespacho == obsRedespacho),
            Arg.Any<CancellationToken>());

        // Verifica notificación de campanita (MensajeChatEnviadoEvent hacia Compras + Capataz)
        await publishSpy.Received(1).Publish<MensajeChatEnviadoEvent>(
            Arg.Is<MensajeChatEnviadoEvent>(e =>
                e.OrdenCompraId == ordenId &&
                e.CorrelationId == cidRedespacho &&
                e.Destinatarios.Roles.Contains("Comprador") &&
                e.Destinatarios.Roles.Contains("Admin") &&
                e.Destinatarios.Roles.Contains("Capataz") &&
                e.Fragmento.Contains("remanente", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());

        // Verifica aviso automático en el chat de la orden y SignalR
        var mensajesChat = await clientComprador.GetFromJsonAsync<List<MensajeChatOrdenDto>>(
            $"/api/ordenes-compra/{ordenId}/chat/mensajes");
        Assert.NotNull(mensajesChat);
        Assert.Contains(mensajesChat, m =>
            m.EsSistema &&
            m.Texto.Contains("remanente", StringComparison.OrdinalIgnoreCase) &&
            m.Texto.Contains("#2", StringComparison.Ordinal) &&
            m.Texto.Contains(obsRedespacho, StringComparison.Ordinal));

        await notifierSpy.Received().NotificarMensajeAsync(
            Arg.Is<MensajeChatOrdenDto>(m =>
                m.OrdenCompraId == ordenId &&
                m.EsSistema &&
                m.Texto.Contains("remanente", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemanenteEncadenadoYBloqueos409_MultiplesParcialesPermitidos_CompletaYDobleDespachoRechazados()
    {
        Guid ordenId = Guid.Empty;
        Guid obraId = Guid.Empty;
        Guid proveedorId = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, out _, db =>
        {
            (ordenId, obraId, proveedorId) = SembrarOrdenEmitida(db);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // Despacho #1
        var respDesp1 = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, respDesp1.StatusCode);

        // Intento inmediato sin recepción parcial -> 409 Conflict
        var cidDoble1 = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cidDoble1);
        var respDoble1 = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        await AssertProblemDetailsWithCorrelationIdAsync(respDoble1, HttpStatusCode.Conflict, cidDoble1);

        // Recepción parcial #1 -> Re-despacho #2 (200 OK)
        await SimularRecepcionAsync(factory.Services, ordenId, obraId, ordenCompleta: false);
        var respDesp2 = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, respDesp2.StatusCode);
        var dto2 = await respDesp2.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.Equal(2, dto2!.NumeroDespacho);

        // Intento inmediato tras re-despacho #2 sin nueva recepción parcial -> 409 Conflict
        var cidDoble2 = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cidDoble2);
        var respDoble2 = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        await AssertProblemDetailsWithCorrelationIdAsync(respDoble2, HttpStatusCode.Conflict, cidDoble2);

        // Recepción parcial #2 -> Re-despacho #3 (200 OK)
        await SimularRecepcionAsync(factory.Services, ordenId, obraId, ordenCompleta: false);
        var respDesp3 = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        Assert.Equal(HttpStatusCode.OK, respDesp3.StatusCode);
        var dto3 = await respDesp3.Content.ReadFromJsonAsync<OrdenCompraResponseDto>();
        Assert.Equal(3, dto3!.NumeroDespacho);

        // Recepción completa -> cierra definitivamente el ciclo de despachos
        await SimularRecepcionAsync(factory.Services, ordenId, obraId, ordenCompleta: true);
        var cidCompleta = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Remove("X-Correlation-ID");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cidCompleta);
        var respPostCompleta = await client.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null);
        await AssertProblemDetailsWithCorrelationIdAsync(respPostCompleta, HttpStatusCode.Conflict, cidCompleta);

        // Total de publicaciones de OrdenCompraDespachadaEvent = exactamente 3 (ninguna en los 409)
        await publishSpy.Received(3).Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Redespacho_RbacYPropiedad_OtroProveedor403_Capataz403_SinToken401()
    {
        Guid ordenId = Guid.Empty;
        Guid obraId = Guid.Empty;
        Guid proveedorDueno = Guid.Empty;

        using var factory = CreateIsolatedFactory(out var publishSpy, out _, db =>
        {
            (ordenId, obraId, proveedorDueno) = SembrarOrdenEmitida(db);
        });

        // Dejar la orden lista para re-despacho
        var clientDueno = factory.CreateClient();
        clientDueno.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorDueno));
        Assert.Equal(HttpStatusCode.OK, (await clientDueno.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null)).StatusCode);
        await SimularRecepcionAsync(factory.Services, ordenId, obraId, ordenCompleta: false);

        // Otro proveedor -> 403
        var cidOtro = Guid.NewGuid().ToString();
        var clientOtro = factory.CreateClient();
        clientOtro.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", Guid.NewGuid()));
        clientOtro.DefaultRequestHeaders.Add("X-Correlation-ID", cidOtro);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientOtro.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null),
            HttpStatusCode.Forbidden,
            cidOtro);

        // Capataz -> 403
        var cidCapataz = Guid.NewGuid().ToString();
        var clientCapataz = factory.CreateClient();
        clientCapataz.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        clientCapataz.DefaultRequestHeaders.Add("X-Correlation-ID", cidCapataz);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientCapataz.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null),
            HttpStatusCode.Forbidden,
            cidCapataz);

        // Sin token -> 401
        var cid401 = Guid.NewGuid().ToString();
        var clientAnon = factory.CreateClient();
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cid401);
        await AssertProblemDetailsWithCorrelationIdAsync(
            await clientAnon.PostAsync($"/api/ordenes-compra/{ordenId}/despachar", null),
            HttpStatusCode.Unauthorized,
            cid401);

        // Ninguno de los intentos rechazados publicó un segundo evento
        await publishSpy.Received(1).Publish<OrdenCompraDespachadaEvent>(
            Arg.Any<OrdenCompraDespachadaEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_SiFallaSaveChangesAlRedespachar_PropagaExcepcionYNoConfirmaOutbox()
    {
        var dbName = $"DB_Redespacho_Rollback_{Guid.NewGuid()}";
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
            Estado = "Despachada",
            NumeroDespacho = 1,
            FechaEmision = DateTime.UtcNow.AddDays(-1),
            FechaDespacho = DateTime.UtcNow.AddHours(-5),
            DespachadaPor = proveedorId,
            TieneRecepcionParcialPendiente = true,
            FechaUltimaRecepcionParcial = DateTime.UtcNow.AddHours(-1)
        };

        ordenRepo.GetByIdAsync(ordenId, Arg.Any<CancellationToken>()).Returns(orden);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Fallo simulado al confirmar re-despacho"));

        var estadoPresupuestario = Substitute.For<ObraSync.Licitaciones.Application.Interfaces.IEstadoPresupuestarioOrden>();
        estadoPresupuestario.EstaBloqueadaAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<bool?>(false));
        var handler = new DespacharOrdenCompraCommandHandler(ordenRepo, publishEndpoint, unitOfWork, estadoPresupuestario);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.Handle(new DespacharOrdenCompraCommand(ordenId, proveedorId, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(0, await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
    }
}
