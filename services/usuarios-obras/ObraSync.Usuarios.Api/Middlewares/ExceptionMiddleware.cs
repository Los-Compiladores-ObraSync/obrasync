using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Api.Middlewares;

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
        // Resuelve StatusCode, Title, Detail y Type (URI RFC 7807) en una tupla limpia
        var (statusCode, title, detail, type) = exception switch
        {
            ArgumentException ex => (
                StatusCodes.Status400BadRequest,
                "Petición inválida",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            ),
            UnauthorizedAccessException ex => (
                StatusCodes.Status401Unauthorized,
                "No autorizado",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            ),
            ForbiddenException ex => (
                StatusCodes.Status403Forbidden,
                "Acceso prohibido",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            ),
            KeyNotFoundException ex => (
                StatusCodes.Status404NotFound,
                "Recurso no encontrado",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.5"
            ),
            InvalidOperationException ex => (
                StatusCodes.Status409Conflict,
                "Conflicto en la operación",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10"
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

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        // Propagación del correlationId exigido por la arquitectura de ObraSync
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