using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ObraSync.Usuarios.Api.Middlewares;
using Xunit;

namespace ObraSync.Usuarios.Tests;

/// <summary>#104: el servicio de Usuarios devuelve y registra el mismo X-Correlation-ID que le manda el Gateway.</summary>
public class CorrelationIdMiddlewareTests
{
    private sealed class LoggerQueAnotaScopes : ILogger<CorrelationIdMiddleware>
    {
        public List<string> Scopes { get; } = new();
        public List<string> Mensajes { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state.ToString() ?? "");
            if (state is IEnumerable<KeyValuePair<string, object>> pares)
                Scopes.AddRange(pares.Select(p => $"{p.Key}={p.Value}"));
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensajes.Add(formatter(state, exception));
    }

    private static async Task<(HttpContext Contexto, LoggerQueAnotaScopes Logger)> EjecutarAsync(string? header)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Method = "GET";
        contexto.Request.Path = "/api/obras";
        if (header is not null)
            contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = header;

        var logger = new LoggerQueAnotaScopes();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);
        await middleware.InvokeAsync(contexto);
        return (contexto, logger);
    }

    [Fact]
    public async Task Con_un_id_valido_lo_conserva_lo_devuelve_y_lo_registra()
    {
        var id = Guid.NewGuid().ToString();

        var (contexto, logger) = await EjecutarAsync($"  {id}  ");

        Assert.Equal(id, contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
        Assert.Equal(id, contexto.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
        Assert.Contains(logger.Scopes, s => s.Contains(id));
        Assert.Contains(logger.Mensajes, m => m.Contains(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacios y /raros")]
    [InlineData("linea1\nlinea2")]
    public async Task Sin_un_id_valido_genera_un_GUID_y_lo_devuelve(string? header)
    {
        var (contexto, _) = await EjecutarAsync(header);

        var devuelto = contexto.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString();
        Assert.True(Guid.TryParse(devuelto, out var guid));
        Assert.NotEqual(Guid.Empty, guid);
        Assert.Equal(devuelto, contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString());
    }

    [Fact]
    public async Task Un_id_de_mas_de_64_caracteres_se_reemplaza()
    {
        var (contexto, _) = await EjecutarAsync(new string('a', 65));

        Assert.True(Guid.TryParse(contexto.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString(), out _));
    }
}
