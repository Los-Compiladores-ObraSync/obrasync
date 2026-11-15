using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Domain.Exceptions;

namespace ObraSync.Recepcion.Api.Middlewares;

/// <summary>
/// Middleware centralizado de excepciones para Recepción y Control Presupuestario (#94, #136).
/// Captura excepciones de dominio y validación, formateándolas como respuestas RFC 7807 (ProblemDetails)
/// con propagación obligatoria de CorrelationId, permitiendo controladores skinny ("aburridos").
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            RecepcionConflictoException ex => (
                StatusCodes.Status409Conflict,
                "Conflicto en la operación",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            TransicionEstadoInvalidaException ex => (
                StatusCodes.Status409Conflict,
                "Transición de estado inválida",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
            ),
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "Petición inválida",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            ),
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "Recurso no encontrado",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            ),
            UnauthorizedAccessException ex => (
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.2"
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
            _logger.LogWarning("La respuesta ya comenzó; no se pudo escribir ProblemDetails para {ExceptionType}", exception.GetType().Name);
            return;
        }

        context.Response.Clear();
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? context.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Type = type,
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }
}
