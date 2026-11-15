using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Recepcion.Api.Middlewares;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Domain.Exceptions;
using Xunit;

namespace ObraSync.Recepcion.Tests;

public class ExceptionMiddlewareTests
{
    private static async Task<(int status, string cuerpo)> EjecutarAsync(
        Exception aLanzar,
        string? correlationIdHeader = null)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        contexto.Request.Path = "/api/test";

        if (!string.IsNullOrWhiteSpace(correlationIdHeader))
        {
            contexto.Request.Headers["X-Correlation-ID"] = correlationIdHeader;
        }

        var middleware = new ExceptionMiddleware(
            _ => throw aLanzar,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var cuerpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        return (contexto.Response.StatusCode, cuerpo);
    }

    [Fact]
    public async Task RecepcionConflictoException_Devuelve409ConflictConDetalleYCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var (status, cuerpo) = await EjecutarAsync(
            new RecepcionConflictoException("La orden de compra ya fue recibida por completo."),
            correlationId);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("La orden de compra ya fue recibida por completo.", cuerpo);

        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task TransicionEstadoInvalidaException_Devuelve409Conflict()
    {
        var (status, cuerpo) = await EjecutarAsync(
            new TransicionEstadoInvalidaException("No se puede pasar de Completa a Pendiente."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("No se puede pasar de Completa a Pendiente.", cuerpo);
    }

    [Fact]
    public async Task ArgumentException_Devuelve400BadRequestConDetalle()
    {
        var (status, cuerpo) = await EjecutarAsync(
            new ArgumentException("El número de remito es obligatorio."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("El número de remito es obligatorio.", cuerpo);
    }

    [Fact]
    public async Task KeyNotFoundException_Devuelve404NotFoundConDetalle()
    {
        var (status, cuerpo) = await EjecutarAsync(
            new KeyNotFoundException("No se encontró el recurso solicitado."));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.Contains("No se encontró el recurso solicitado.", cuerpo);
    }

    [Fact]
    public async Task UnauthorizedAccessException_Devuelve401Unauthorized()
    {
        var (status, cuerpo) = await EjecutarAsync(
            new UnauthorizedAccessException("Token no válido o revocado."));

        Assert.Equal(StatusCodes.Status401Unauthorized, status);
        Assert.Contains("Token no válido o revocado.", cuerpo);
    }

    [Fact]
    public async Task ExcepcionGenericaNoControlada_Devuelve500GenericoSinFiltrarDetallesInternos()
    {
        var excepcionInterna = new InvalidOperationException("Cadena de conexión sensible Server=10.0.0.1;Password=SuperSecret;");
        var (status, cuerpo) = await EjecutarAsync(excepcionInterna);

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("SuperSecret", cuerpo);
        Assert.Contains("Ocurrió un error inesperado en el servidor.", cuerpo);

        using var json = JsonDocument.Parse(cuerpo);
        Assert.True(json.RootElement.TryGetProperty("correlationId", out _));
    }
}
