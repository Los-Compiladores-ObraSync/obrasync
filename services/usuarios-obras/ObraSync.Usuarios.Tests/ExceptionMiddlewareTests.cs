using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Usuarios.Api.Middlewares;
using ObraSync.Usuarios.Domain.Exceptions;
using Xunit;

namespace ObraSync.Usuarios.Tests;

/// <summary>Punto 9 de #136 y refactor OCP: mapeo extensible de excepciones a ProblemDetails.</summary>
public class ExceptionMiddlewareTests
{
    private static async Task<(int status, string cuerpo)> EjecutarAsync(
        Exception aLanzar,
        IExceptionProblemDetailsMapper? mapper = null)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        contexto.Request.Path = "/api/prueba";
        contexto.Request.Headers["X-Correlation-ID"] = "corr-test-ocp";

        var middleware = new ExceptionMiddleware(
            _ => throw aLanzar,
            NullLogger<ExceptionMiddleware>.Instance,
            mapper);

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var cuerpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        return (contexto.Response.StatusCode, cuerpo);
    }

    [Fact]
    public async Task ValidacionException_Devuelve400ConSuMensaje()
    {
        var (status, cuerpo) = await EjecutarAsync(new ValidacionException("El rol 'X' no es válido."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("El rol 'X' no es válido.", cuerpo);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Petición inválida", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.1", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_Devuelve401ConSuMensajeYRfc()
    {
        var (status, cuerpo) = await EjecutarAsync(new UnauthorizedAccessException("Credenciales inválidas."));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("No autorizado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Credenciales inválidas.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.2", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ForbiddenException_Devuelve403ConSuMensajeYRfc()
    {
        var (status, cuerpo) = await EjecutarAsync(new ForbiddenException("Sin permisos."));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Acceso prohibido", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Sin permisos.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task KeyNotFoundException_Devuelve404ConSuMensajeYRfc()
    {
        var (status, cuerpo) = await EjecutarAsync(new KeyNotFoundException("Obra no encontrada."));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Recurso no encontrado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Obra no encontrada.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.5", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ConflictoException_Devuelve409ConSuMensaje()
    {
        var (status, cuerpo) = await EjecutarAsync(new ConflictoException("Ya existe un usuario registrado."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("Ya existe un usuario registrado.", cuerpo);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Conflicto en la operación", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.10", json.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task ServicioNoDisponibleException_Devuelve503ConSuMensajeYRfc()
    {
        var (status, cuerpo) = await EjecutarAsync(new ServicioNoDisponibleException("VAPID no configurado."));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Servicio no disponible", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("VAPID no configurado.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.4", json.RootElement.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    public async Task ExcepcionDelFrameworkSinTipoPropio_Devuelve500GenericoSinFiltrarElMensaje(Type tipo)
    {
        var excepcion = (Exception)Activator.CreateInstance(tipo, "cadena de conexion Server=interno;Password=secreto")!;

        var (status, cuerpo) = await EjecutarAsync(excepcion);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("secreto", cuerpo);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Error interno del servidor", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Ocurrió un error inesperado en el servidor.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.1", json.RootElement.GetProperty("type").GetString());
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task MapperExtensible_PermiteRegistrarNuevaEstrategiaSinModificarMiddleware()
    {
        var estrategias = ExceptionProblemDetailsMapper.GetDefaultStrategies()
            .Concat(new IExceptionMappingStrategy[] { new CustomRateLimitStrategy() });
        var mapper = new ExceptionProblemDetailsMapper(estrategias);

        var (status, cuerpo) = await EjecutarAsync(new CustomRateLimitException("Demasiados intentos."), mapper);

        Assert.Equal(StatusCodes.Status429TooManyRequests, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("Límite de peticiones excedido", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Demasiados intentos.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", json.RootElement.GetProperty("type").GetString());
        Assert.Equal("corr-test-ocp", json.RootElement.GetProperty("correlationId").GetString());
    }

    private sealed class CustomRateLimitException : Exception
    {
        public CustomRateLimitException(string message) : base(message) { }
    }

    private sealed class CustomRateLimitStrategy : ExceptionMappingStrategy<CustomRateLimitException>
    {
        protected override ExceptionProblemMapping Map(CustomRateLimitException exception) => new(
            StatusCodes.Status429TooManyRequests,
            "Límite de peticiones excedido",
            exception.Message,
            "https://tools.ietf.org/html/rfc6585#section-4");
    }
}
