using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Requisiciones.Api.Middlewares;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ConHeaderValido_ConservaHeaderLoPropagaEnRespuestaYScope()
    {
        var correlationId = Guid.NewGuid().ToString();
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "GET";
        contexto.Request.Path = "/api/requisiciones";
        contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = $"  {correlationId}  ";

        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

        var nextEjecutado = false;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            nextEjecutado = true;
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(contexto);

        Assert.True(nextEjecutado);
        Assert.Equal(correlationId, contexto.Items[CorrelationIdMiddleware.CorrelationIdItemKey]);
        Assert.Equal(correlationId, contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
        Assert.Equal(correlationId, contexto.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
        Assert.Contains(loggerProvider.Entries, e =>
            e.Scopes.Any(s => s.Contains(correlationId, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_SinHeaderValido_GeneraGuidYLoPropaga(string? headerInvalido)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "POST";
        contexto.Request.Path = "/api/catalogo/insumos";

        if (headerInvalido is not null)
        {
            contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = headerInvalido;
        }

        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(contexto);

        var generado = contexto.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string;
        Assert.False(string.IsNullOrWhiteSpace(generado));
        Assert.True(Guid.TryParse(generado, out var guid));
        Assert.NotEqual(Guid.Empty, guid);
        Assert.Equal(generado, contexto.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
    }

    [Fact]
    public async Task InvokeAsync_ConHeaderDemasiadoLargo_GeneraGuidNuevo()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "GET";
        contexto.Request.Path = "/health";
        contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = new string('x', 65);

        var loggerProvider = new InMemoryLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));
        var logger = loggerFactory.CreateLogger<CorrelationIdMiddleware>();

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(contexto);

        var generado = contexto.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string;
        Assert.True(Guid.TryParse(generado, out var guid));
        Assert.NotEqual(Guid.Empty, guid);
    }

    [Fact]
    public async Task PipelineHttp_SinHeaderYConErrorDeValidacion_GeneraCorrelationIdEnHeaderYProblemDetails()
    {
        const string jwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        var dbName = $"DB_Req_Corr_{Guid.NewGuid()}";

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", jwtSecret);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<RequisicionesDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                var configuraciones = services.Where(d =>
                        d.ServiceType.IsGenericType &&
                        d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration") &&
                        d.ServiceType.GenericTypeArguments[0] == typeof(RequisicionesDbContext))
                    .ToList();
                foreach (var configuracion in configuraciones) services.Remove(configuracion);

                services.AddDbContext<RequisicionesDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });

        var client = factory.CreateClient();

        // 1. /health sin header genera GUID en X-Correlation-ID de respuesta
        var healthResp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
        Assert.True(healthResp.Headers.TryGetValues(CorrelationIdMiddleware.CorrelationIdHeader, out var healthHeaders));
        Assert.True(Guid.TryParse(healthHeaders.Single(), out var generatedGuid));
        Assert.NotEqual(Guid.Empty, generatedGuid);

        // 2. POST inválido sin header llega a ExceptionMiddleware con el mismo correlationId generado
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
            claims: new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("name", "Admin"),
                new Claim("role", "Admin")
            },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var errorResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
        {
            Codigo = "MAT-ERR-001",
            Descripcion = "Insumo inválido",
            UnidadMedida = "LITRO"
        });

        Assert.Equal(HttpStatusCode.BadRequest, errorResp.StatusCode);
        Assert.True(errorResp.Headers.TryGetValues(CorrelationIdMiddleware.CorrelationIdHeader, out var errHeaders));
        var errHeaderId = errHeaders.Single();
        Assert.True(Guid.TryParse(errHeaderId, out _));

        using var doc = JsonDocument.Parse(await errorResp.Content.ReadAsStringAsync());
        Assert.Equal(errHeaderId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    private sealed record CapturedLogEntry(string Message, IReadOnlyList<string> Scopes);

    private sealed class InMemoryLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new InMemoryLogger(Entries);

        public void Dispose() { }

        private sealed class InMemoryLogger : ILogger
        {
            private readonly ConcurrentQueue<CapturedLogEntry> _entries;
            private readonly System.Threading.AsyncLocal<Stack<string>> _scopes = new();

            public InMemoryLogger(ConcurrentQueue<CapturedLogEntry> entries) => _entries = entries;

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
                _entries.Enqueue(new CapturedLogEntry(
                    formatter(state, exception),
                    _scopes.Value?.ToArray() ?? Array.Empty<string>()));
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
}
