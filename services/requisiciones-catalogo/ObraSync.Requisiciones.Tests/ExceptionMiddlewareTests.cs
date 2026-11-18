using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Requisiciones.Api.Middlewares;
using ObraSync.Requisiciones.Application.Exceptions;
using Xunit;

namespace ObraSync.Requisiciones.Tests;

public class ExceptionMiddlewareTests
{
    private static async Task<(int status, string contentType, string cuerpo)> EjecutarAsync(
        Exception aLanzar,
        string? correlationIdHeader = null)
    {
        var contexto = new DefaultHttpContext();
        contexto.Response.Body = new MemoryStream();
        contexto.Request.Path = "/api/requisiciones";

        if (!string.IsNullOrWhiteSpace(correlationIdHeader))
        {
            contexto.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader] = correlationIdHeader;
        }

        var middleware = new ExceptionMiddleware(
            _ => throw aLanzar,
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(contexto);

        contexto.Response.Body.Position = 0;
        var cuerpo = await new StreamReader(contexto.Response.Body).ReadToEndAsync();
        return (contexto.Response.StatusCode, contexto.Response.ContentType ?? string.Empty, cuerpo);
    }

    [Fact]
    public async Task RequisicionDuplicadaException_Devuelve409ConExtensionesYCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var reqExistenteId = Guid.NewGuid();

        var (status, contentType, cuerpo) = await EjecutarAsync(
            new RequisicionDuplicadaException("REQ-2026-0042", reqExistenteId, "Requisición duplicada detectada."),
            correlationId);

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.StartsWith("application/problem+json", contentType);

        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(409, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Requisición duplicada", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("REQ-2026-0042", json.RootElement.GetProperty("numeroRequisicion").GetString());
        Assert.Equal(reqExistenteId, json.RootElement.GetProperty("requisicionId").GetGuid());
        Assert.Equal(correlationId, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task DbUpdateConcurrencyException_Devuelve409ConflictConMensajeAmigable()
    {
        var (status, _, cuerpo) = await EjecutarAsync(
            new DbUpdateConcurrencyException("Conflicto de RowVersion"));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("La requisición cambió, recargá e intentá nuevamente.", cuerpo);
    }

    [Fact]
    public async Task ObraAsignacionNoVerificableException_Devuelve503ServiceUnavailable()
    {
        var (status, _, cuerpo) = await EjecutarAsync(
            new ObraAsignacionNoVerificableException("Timeout al consultar Usuarios"));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal("No se pudo verificar la obra", json.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task InvalidOperationException_Devuelve409ConflictConDetalle()
    {
        var (status, _, cuerpo) = await EjecutarAsync(
            new InvalidOperationException("Solo se pueden aprobar requisiciones pendientes."));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("Solo se pueden aprobar requisiciones pendientes.", cuerpo);
    }

    [Theory]
    [InlineData("No se encontró la requisición solicitada.", "Requisición no encontrada")]
    [InlineData("No se encontró el artículo con Id '123' en el catálogo.", "Artículo de catálogo inexistente")]
    public async Task KeyNotFoundException_Devuelve404NotFoundConTituloApropiado(string mensaje, string tituloEsperado)
    {
        var (status, _, cuerpo) = await EjecutarAsync(new KeyNotFoundException(mensaje));

        Assert.Equal(StatusCodes.Status404NotFound, status);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(tituloEsperado, json.RootElement.GetProperty("title").GetString());
        Assert.Equal(mensaje, json.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task UnauthorizedAccessException_Devuelve403Forbidden()
    {
        var (status, _, cuerpo) = await EjecutarAsync(
            new UnauthorizedAccessException("Solo el capataz solicitante dueño puede editarla."));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Contains("Solo el capataz solicitante dueño puede editarla.", cuerpo);
    }

    [Fact]
    public async Task ArgumentException_Devuelve400BadRequest()
    {
        var (status, _, cuerpo) = await EjecutarAsync(
            new ArgumentException("El código del insumo no puede estar vacío."));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("El código del insumo no puede estar vacío.", cuerpo);
    }

    [Fact]
    public async Task ArgumentNullExceptionYExcepcionInesperada_Devuelven500SinFiltrarDetalles()
    {
        var (statusNull, _, cuerpoNull) = await EjecutarAsync(
            new ArgumentNullException("paramInterno", "Detalle interno sensible"));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusNull);
        Assert.DoesNotContain("Detalle interno sensible", cuerpoNull);
        Assert.Contains("Ocurrió un error inesperado en el servidor.", cuerpoNull);

        var (statusGen, _, cuerpoGen) = await EjecutarAsync(
            new Exception("ConnectionString=Password=SuperSecret"));

        Assert.Equal(StatusCodes.Status500InternalServerError, statusGen);
        Assert.DoesNotContain("SuperSecret", cuerpoGen);
        Assert.Contains("Ocurrió un error inesperado en el servidor.", cuerpoGen);
    }
}
