using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
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
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Handlers;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>
/// Tarjeta #104: Propagar el X-Correlation-ID hasta los mensajes de RabbitMQ (OrdenCompraEmitidaEvent) y los logs.
/// </summary>
public class CorrelationIdPropagacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName;
    private readonly IPublishEndpoint _publishSpy = Substitute.For<IPublishEndpoint>();
    private readonly InMemoryLoggerProvider _loggerProvider = new();

    public CorrelationIdPropagacionTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _dbName = $"DB_Licit_Correlation_{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<ILoggerProvider>(_loggerProvider);

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<LicitacionesDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IDbContextOptionsConfiguration<LicitacionesDbContext>>();

                services.AddDbContext<LicitacionesDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_dbName);
                });

                services.RemoveAll<IPublishEndpoint>();
                services.AddSingleton(_publishSpy);
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

    private async Task<(Licitacion Licitacion, Cotizacion Cotizacion)> SembrarLicitacionYCotizacionAsync()
    {
        var articuloId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();

        var licitacion = new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0300",
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    LicitacionId = licitacionId,
                    ArticuloId = articuloId,
                    Descripcion = "Cemento Portland",
                    Cantidad = 40m,
                    UnidadMedida = "BOLSA"
                }
            }
        };

        var cotizacion = new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            ProveedorId = Guid.NewGuid(),
            Flete = 500m,
            PlazoEntregaDias = 3,
            FechaCreacion = DateTime.UtcNow.AddHours(-1),
            Items = new List<CotizacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    CotizacionId = cotizacionId,
                    ArticuloId = articuloId,
                    PrecioUnitario = 100m
                }
            }
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        db.Licitaciones.Add(licitacion);
        db.Cotizaciones.Add(cotizacion);
        await db.SaveChangesAsync();

        return (licitacion, cotizacion);
    }

    [Fact]
    public async Task Adjudicar_ConHeaderXCorrelationId_PropagaMismoCorrelationIdAlEventoOrdenCompraEmitidaYAlHeaderDeRespuesta()
    {
        var (licitacion, cotizacion) = await SembrarLicitacionYCotizacionAsync();
        var correlationIdEsperado = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationIdEsperado.ToString());

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/adjudicaciones",
            new { cotizacionId = cotizacion.CotizacionId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 1. La respuesta HTTP devuelve el mismo X-Correlation-ID enviado por el cliente
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        Assert.Equal(correlationIdEsperado.ToString(), headerValues.Single());

        // 2. El evento OrdenCompraEmitidaEvent publicado lleva exactamente ese CorrelationId
        await _publishSpy.Received(1).Publish<OrdenCompraEmitidaEvent>(
            Arg.Is<OrdenCompraEmitidaEvent>(e =>
                e.LicitacionId == licitacion.LicitacionId &&
                e.CorrelationId == correlationIdEsperado),
            Arg.Any<CancellationToken>());

        // 3. Los logs registraron el CorrelationId durante la adjudicación
        Assert.Contains(_loggerProvider.Entries, entry =>
            entry.Message.Contains(correlationIdEsperado.ToString(), StringComparison.OrdinalIgnoreCase) ||
            entry.Scopes.Any(s => s.Contains(correlationIdEsperado.ToString(), StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Adjudicar_SinHeaderXCorrelationId_GeneraCorrelationIdLoDevuelveEnRespuestaYLoPropagaAlEvento()
    {
        var (licitacion, cotizacion) = await SembrarLicitacionYCotizacionAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/adjudicaciones",
            new { cotizacionId = cotizacion.CotizacionId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 1. El middleware generó un X-Correlation-ID válido y lo devolvió en el header de respuesta
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var headerValues));
        var headerCorrelationId = headerValues.Single();
        Assert.True(Guid.TryParse(headerCorrelationId, out var correlationGuidGenerado));
        Assert.NotEqual(Guid.Empty, correlationGuidGenerado);

        // 2. Ese mismo CorrelationId generado viajó en el OrdenCompraEmitidaEvent publicado
        await _publishSpy.Received(1).Publish<OrdenCompraEmitidaEvent>(
            Arg.Is<OrdenCompraEmitidaEvent>(e =>
                e.LicitacionId == licitacion.LicitacionId &&
                e.CorrelationId == correlationGuidGenerado),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequisicionAprobadaConsumer_RegistraCorrelationIdDelEventoEnScopeYMensajeDeLog()
    {
        var options = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase($"DB_Consumer_Corr_{Guid.NewGuid()}")
            .Options;
        await using var db = new LicitacionesDbContext(options);

        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<RequisicionAprobadaConsumer>();

        var consumer = new RequisicionAprobadaConsumer(db, logger);
        var correlationId = Guid.NewGuid();
        var evento = new RequisicionAprobadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            RequisicionId: Guid.NewGuid(),
            NumeroRequisicion: "REQ-2026-0400",
            ObraId: Guid.NewGuid(),
            SolicitanteId: Guid.NewGuid(),
            AprobadorId: Guid.NewGuid(),
            Items: new List<RequisicionAprobadaItemEvent>
            {
                new(Guid.NewGuid(), "Cemento", 50m, "BOLSA")
            },
            Etapa: "Estructura");

        var context = Substitute.For<ConsumeContext<RequisicionAprobadaEvent>>();
        context.Message.Returns(evento);

        await consumer.Consume(context);

        Assert.Contains(loggerProvider.Entries, entry =>
            entry.Message.Contains(correlationId.ToString(), StringComparison.OrdinalIgnoreCase) &&
            entry.Scopes.Any(s => s.Contains(correlationId.ToString(), StringComparison.OrdinalIgnoreCase)));
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
