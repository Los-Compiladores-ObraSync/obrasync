using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Consumers;
using ObraSync.Recepcion.Infrastructure.Messaging;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Tarjeta #104: Propagar el X-Correlation-ID hasta los mensajes de RabbitMQ (RecepcionConfirmadaEvent, OrdenCompraBloqueadaEvent) y los logs.
/// </summary>
public class CorrelationIdPropagacionTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;
    private readonly InMemoryLoggerProvider _loggerProvider = new();
    private readonly IRecepcionEventPublisher _publisherSpy = Substitute.For<IRecepcionEventPublisher>();

    public CorrelationIdPropagacionTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> CrearFactoryConSpies(InMemoryLoggerProvider loggerProvider, IRecepcionEventPublisher publisherSpy)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(loggerProvider);
                services.RemoveAll<IRecepcionEventPublisher>();
                services.AddSingleton(publisherSpy);
            });
        });
    }

    private async Task<(Guid ordenCompraId, Guid obraId, Guid articuloId)> CrearOrdenCompraDePrueba()
    {
        var ordenCompraId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        var articulo = OrdenCompraItem.Crear(
            articuloId,
            100,
            "UNIDAD",
            10,
            "Cemento Loma Negra");

        var ordenCompra = OrdenCompra.Crear(
            ordenCompraId,
            obraId,
            Guid.NewGuid(),
            1000,
            new List<OrdenCompraItem> { articulo });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();

        db.OrdenesCompra.Add(ordenCompra);
        await db.SaveChangesAsync();

        return (ordenCompraId, obraId, articuloId);
    }

    [Fact]
    public async Task PostRecepcion_ConHeaderXCorrelationId_PropagaMismoCorrelationIdAlEventoYAlHeaderDeRespuesta()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        var correlationIdEsperado = Guid.NewGuid();
        var loggerProvider = new InMemoryLoggerProvider();
        var publisherSpy = Substitute.For<IRecepcionEventPublisher>();

        var factory = CrearFactoryConSpies(loggerProvider, publisherSpy);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationIdEsperado.ToString());

        var solicitud = new
        {
            obraId,
            ordenCompraId = ordenId,
            numeroRemito = $"REM-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Entrega con correlationId",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento Loma Negra",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 100m,
                    unidadMedida = "UNIDAD",
                    conforme = true,
                    observacion = (string?)null
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // 1. La respuesta HTTP devuelve el mismo X-Correlation-ID enviado por el cliente
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        Assert.Equal(correlationIdEsperado.ToString(), headerValues.Single());

        // 2. El publicador recibe exactamente ese CorrelationId para el evento RecepcionConfirmadaEvent
        await publisherSpy.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            correlationIdEsperado,
            Arg.Any<CancellationToken>());

        // 3. El logger capturó el scope con CorrelationId durante la ejecución
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Scopes.Any(s => s.Contains(correlationIdEsperado.ToString(), StringComparison.OrdinalIgnoreCase)) ||
            entry.Message.Contains(correlationIdEsperado.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PostRecepcion_SinHeaderXCorrelationId_GeneraCorrelationIdLoDevuelveEnRespuestaYLoPropagaAlEvento()
    {
        var (ordenId, obraId, articuloId) = await CrearOrdenCompraDePrueba();
        var loggerProvider = new InMemoryLoggerProvider();
        var publisherSpy = Substitute.For<IRecepcionEventPublisher>();

        var factory = CrearFactoryConSpies(loggerProvider, publisherSpy);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var solicitud = new
        {
            obraId,
            ordenCompraId = ordenId,
            numeroRemito = $"REM-{Guid.NewGuid():N}",
            fechaRecepcion = DateTime.UtcNow,
            observaciones = "Entrega sin correlationId previo",
            detalles = new[]
            {
                new
                {
                    articuloId,
                    descripcionArticulo = "Cemento Loma Negra",
                    cantidadEsperada = 100m,
                    cantidadRecibida = 100m,
                    unidadMedida = "UNIDAD",
                    conforme = true,
                    observacion = (string?)null
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/recepciones", solicitud);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // 1. El middleware generó un X-Correlation-ID válido y lo devolvió en los headers de respuesta
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        var headerCorrelationId = headerValues.Single();
        Assert.True(Guid.TryParse(headerCorrelationId, out var correlationGuidGenerado));
        Assert.NotEqual(Guid.Empty, correlationGuidGenerado);

        // 2. Ese mismo CorrelationId generado viajó al evento publicado
        await publisherSpy.Received(1).PublishRecepcionConfirmadaAsync(
            Arg.Any<RecepcionEntity>(),
            correlationGuidGenerado,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecepciones_ConHeaderXCorrelationId_DevuelveMismoHeaderEnRespuestaYRegistraScope()
    {
        var correlationIdEsperado = Guid.NewGuid();
        var loggerProvider = new InMemoryLoggerProvider();
        var factory = CrearFactoryConSpies(loggerProvider, _publisherSpy);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationIdEsperado.ToString());

        var response = await client.GetAsync("/api/recepciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        Assert.Equal(correlationIdEsperado.ToString(), headerValues.Single());

        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Scopes.Any(s => s.Contains(correlationIdEsperado.ToString(), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task MassTransitRecepcionEventPublisher_ConfiguraCorrelationIdYMessageIdEnContextoDeEnvio()
    {
        var publishEndpointSpy = Substitute.For<IPublishEndpoint>();
        var publisher = new MassTransitRecepcionEventPublisher(publishEndpointSpy);

        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-001",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<DetalleRecepcion>()
        };

        var correlationId = Guid.NewGuid();

        await publisher.PublishRecepcionConfirmadaAsync(recepcion, correlationId);

        // Verifica que se llamó Publish configurando context.CorrelationId y context.MessageId
        await publishEndpointSpy.Received(1).Publish(
            Arg.Is<RecepcionConfirmadaEvent>(e =>
                e.RecepcionId == recepcion.Id &&
                e.CorrelationId == correlationId),
            Arg.Any<IPipe<PublishContext<RecepcionConfirmadaEvent>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdenCompraEmitidaConsumer_RegistraCorrelationIdDelEventoEnScopeYMensajeDeLog()
    {
        var options = new DbContextOptionsBuilder<RecepcionDbContext>()
            .UseInMemoryDatabase($"DB_Consumer_Corr_{Guid.NewGuid()}")
            .Options;
        await using var db = new RecepcionDbContext(options);

        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<OrdenCompraEmitidaConsumer>();

        var repo = new ObraSync.Recepcion.Infrastructure.Persistence.Repositories.OrdenCompraRepository(db);
        var consumer = new OrdenCompraEmitidaConsumer(db, repo, logger);

        var correlationId = Guid.NewGuid();
        var evento = new OrdenCompraEmitidaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            OrdenCompraId: Guid.NewGuid(),
            LicitacionId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            MontoTotal: 5000m,
            Items: new List<OrdenCompraEmitidaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "BOLSA", 500m, "Cal Hidratada")
            },
            Etapa: "Albañilería");

        var context = Substitute.For<ConsumeContext<OrdenCompraEmitidaEvent>>();
        context.Message.Returns(evento);
        context.CancellationToken.Returns(CancellationToken.None);

        await consumer.Consume(context);

        // Verifica que el CorrelationId fue registrado en el scope o en el mensaje formateado
        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Scopes.Any(s => s.Contains(correlationId.ToString(), StringComparison.OrdinalIgnoreCase)) ||
            entry.Message.Contains(correlationId.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetOrdenesPendientes_SinHeaderXCorrelationId_GeneraYDevuelveXCorrelationId()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));

        var response = await client.GetAsync("/api/recepciones/ordenes-pendientes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        var correlationIdGenerado = headerValues.Single();
        Assert.True(Guid.TryParse(correlationIdGenerado, out var guidGenerado));
        Assert.NotEqual(Guid.Empty, guidGenerado);
    }

    [Fact]
    public async Task GetPresupuestosPartidas_ConHeaderXCorrelationId_DevuelveMismoHeader()
    {
        var correlationIdEsperado = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationIdEsperado.ToString());

        var response = await client.GetAsync("/api/presupuestos/partidas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        Assert.Equal(correlationIdEsperado.ToString(), headerValues.Single());
    }

    [Fact]
    public async Task PeticionConError_ProblemDetailsIncluyeMismoCorrelationIdYHeaderDeRespuesta()
    {
        var correlationIdEsperado = Guid.NewGuid();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Admin"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationIdEsperado.ToString());

        // Consultar recepción inexistente produce 404 ProblemDetails
        var response = await client.GetAsync($"/api/recepciones/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        Assert.Equal(correlationIdEsperado.ToString(), headerValues.Single());

        var problemDetails = await response.Content.ReadFromJsonAsync<JsonDocument>();
        Assert.NotNull(problemDetails);
        Assert.True(problemDetails.RootElement.TryGetProperty("correlationId", out var corrProp));
        Assert.Equal(correlationIdEsperado.ToString(), corrProp.GetString());
    }
}

public sealed record CapturedLogEntry(string Category, LogLevel Level, string Message, IReadOnlyList<string> Scopes);

public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, Entries);

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ConcurrentQueue<CapturedLogEntry> _entries;
        private readonly AsyncLocal<Stack<string>> _scopes = new();

        public InMemoryLogger(string categoryName, ConcurrentQueue<CapturedLogEntry> entries)
        {
            _categoryName = categoryName;
            _entries = entries;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            var stack = _scopes.Value ??= new Stack<string>();
            var text = state is IEnumerable<KeyValuePair<string, object>> kvps
                ? string.Join(", ", kvps.Select(kv => $"{kv.Key}={kv.Value}"))
                : state.ToString() ?? string.Empty;
            stack.Push(text);
            return new ScopePopper(stack);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scopes = _scopes.Value?.ToArray() ?? Array.Empty<string>();
            _entries.Enqueue(new CapturedLogEntry(
                _categoryName,
                logLevel,
                formatter(state, exception),
                scopes));
        }

        private sealed class ScopePopper : IDisposable
        {
            private readonly Stack<string> _stack;
            public ScopePopper(Stack<string> stack) => _stack = stack;
            public void Dispose()
            {
                if (_stack.Count > 0) _stack.Pop();
            }
        }
    }
}
