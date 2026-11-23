using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ObraSync.Requisiciones.Api.Exceptions;
using ObraSync.Requisiciones.Application.Exceptions;

namespace ObraSync.Requisiciones.Api.Middlewares;

/// <summary>
/// Middleware centralizado de excepciones para Requisiciones y Catálogo.
/// Traduce excepciones de dominio, aplicación y concurrencia a respuestas RFC 7807 (ProblemDetails)
/// con propagación de CorrelationId, manteniendo los controladores delgados.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await ManejarExcepcionAsync(context, ex);
        }
    }

    private async Task ManejarExcepcionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, detail, type) = exception switch
        {
            RequisicionDuplicadaException ex => (
                StatusCodes.Status409Conflict,
                "Requisición duplicada",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                "Conflicto de concurrencia en la requisición",
                "La requisición cambió, recargá e intentá nuevamente.",
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            ObraAsignacionNoVerificableException => (
                StatusCodes.Status503ServiceUnavailable,
                "No se pudo verificar la obra",
                "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.4"
            ),
            ObraNoAsignadaException ex => (
                StatusCodes.Status403Forbidden,
                "Obra no asignada",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            ),
            UsuarioNoIdentificadoException ex => (
                StatusCodes.Status401Unauthorized,
                "Usuario no identificado",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            ),
            PaginacionInvalidaException ex => (
                StatusCodes.Status400BadRequest,
                "Paginación inválida",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            ),
            InvalidOperationException ex => (
                StatusCodes.Status409Conflict,
                "Conflicto en la operación",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                ex.Message.Contains("artículo", StringComparison.OrdinalIgnoreCase)
                    ? "Artículo de catálogo inexistente"
                    : "Requisición no encontrada",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            ),
            UnauthorizedAccessException ex => (
                StatusCodes.Status403Forbidden,
                "Acceso denegado: no es el solicitante dueño de la requisición",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            ),
            // ArgumentNullException deriva de ArgumentException: si ocurre por un bug interno debe devolver 500, no 400.
            ArgumentNullException => (
                StatusCodes.Status500InternalServerError,
                "Error interno del servidor",
                "Ocurrió un error inesperado en el servidor.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            ),
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "Datos inválidos",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Error interno del servidor",
                "Ocurrió un error inesperado en el servidor.",
                "https://tools.ietf.org/html/rfc9110#section-15.6.1"
            )
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Error no controlado atrapado en ExceptionMiddleware.");
        }

        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "La respuesta ya comenzó; no se pudo escribir ProblemDetails para {ExceptionType}",
                exception.GetType().Name);
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var correlationId = context.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string
                            ?? context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.TraceIdentifier;
        }

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        if (exception is RequisicionDuplicadaException duplicadaEx)
        {
            problemDetails.Extensions["numeroRequisicion"] = duplicadaEx.NumeroRequisicionExistente;
            problemDetails.Extensions["requisicionId"] = duplicadaEx.RequisicionExistenteId;
        }

        problemDetails.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }
}
