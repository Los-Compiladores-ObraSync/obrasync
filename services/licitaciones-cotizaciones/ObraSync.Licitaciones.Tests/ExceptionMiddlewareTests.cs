using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.Exceptions;

namespace ObraSync.Licitaciones.Tests;

public class ExceptionMiddlewareTests
{
    private static async Task<(int StatusCode, string ContentType, string Body)> EjecutarMiddlewareAsync(
        Exception excepcion,
        IEnumerable<IExceptionProblemMapper>? mappersPersonalizados = null,
        Endpoint? endpoint = null,
        string? correlationIdHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/licitaciones/prueba";

        if (!string.IsNullOrWhiteSpace(correlationIdHeader))
        {
            context.Request.Headers["X-Correlation-ID"] = correlationIdHeader;
        }

        if (endpoint is not null)
        {
            context.SetEndpoint(endpoint);
        }

        IEnumerable<IExceptionProblemMapper> mappers;
        if (mappersPersonalizados is not null)
        {
            mappers = mappersPersonalizados;
        }
        else
        {
            var services = new ServiceCollection();
            services.AddExceptionProblemMapping();
            mappers = services.BuildServiceProvider().GetServices<IExceptionProblemMapper>();
        }

        var middleware = new ExceptionMiddleware(
            _ => throw excepcion,
            mappers,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var cuerpo = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty, cuerpo);
    }

    [Fact]
    public async Task ValidacionException_Devuelve400DatosInvalidos()
    {
        var (status, contentType, body) = await EjecutarMiddlewareAsync(
            new ValidacionException("El plazo debe ser positivo."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.StartsWith("application/problem+json", contentType);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Datos inválidos", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("El plazo debe ser positivo.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NoEncontradoException_Devuelve404NoEncontrado()
    {
        var (status, _, body) = await EjecutarMiddlewareAsync(
            new NoEncontradoException("La cotización no existe."));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("No encontrado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("La cotización no existe.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ConflictoException_Devuelve409Conflicto()
    {
        var (status, _, body) = await EjecutarMiddlewareAsync(
            new ConflictoException("Ya existe una licitación para esa requisición."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Conflicto", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Ya existe una licitación para esa requisición.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_Devuelve409ConflictoDeConcurrencia()
    {
        var (status, _, body) = await EjecutarMiddlewareAsync(
            new DbUpdateConcurrencyException("Conflicto optimista"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Conflicto de concurrencia", json.RootElement.GetProperty("title").GetString());
        Assert.Contains("actualice los datos e intente nuevamente", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_Devuelve403AccesoDenegado()
    {
        var (status, _, body) = await EjecutarMiddlewareAsync(
            new UnauthorizedAccessException("Solo el proveedor dueño puede operar."));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Acceso denegado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Solo el proveedor dueño puede operar.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task EndpointMetadata_SobrescribeMapeoConTituloEspecificoDelEndpoint()
    {
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new MapExceptionProblemAttribute(
                    typeof(KeyNotFoundException),
                    StatusCodes.Status404NotFound,
                    "Licitación no encontrada")),
            "ObtenerPorId");

        var (status, _, body) = await EjecutarMiddlewareAsync(
            new KeyNotFoundException("No se encontró el pliego."),
            endpoint: endpoint);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Licitación no encontrada", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("No se encontró el pliego.", json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ExcepcionInesperada_Devuelve500SinFiltrarDetallesYPropagaCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();

        var (status, contentType, body) = await EjecutarMiddlewareAsync(
            new Exception("SecretConnectionString=Password123!"),
            correlationIdHeader: correlationId);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.DoesNotContain("Password123!", body);

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Error interno del servidor", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Ocurrió un error inesperado al procesar la solicitud.", json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task ExceptionMiddleware_PermiteExtenderConMapperPersonalizadoSinModificarMiddleware()
    {
        var mappers = new IExceptionProblemMapper[] { new TimeoutProblemMapper() };

        var (status, _, body) = await EjecutarMiddlewareAsync(
            new TimeoutException("Demora al consultar el servicio externo."),
            mappersPersonalizados: mappers);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Tiempo de espera agotado", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Demora al consultar el servicio externo.", json.RootElement.GetProperty("detail").GetString());
    }

    private sealed class TimeoutProblemMapper : IExceptionProblemMapper
    {
        public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
        {
            if (exception is TimeoutException ex)
            {
                mapping = new ProblemMapping(
                    StatusCodes.Status504GatewayTimeout,
                    "Tiempo de espera agotado",
                    ex.Message,
                    "https://tools.ietf.org/html/rfc9110#section-15.6.5");
                return true;
            }

            mapping = default!;
            return false;
        }
    }
}
