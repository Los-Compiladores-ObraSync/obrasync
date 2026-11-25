using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ObraSync.Licitaciones.Api.Middlewares;

public sealed class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<IExceptionProblemMapper> _mappers;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        IEnumerable<IExceptionProblemMapper> mappers,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(mappers);
        _mappers = mappers.OrderBy(m => m.Order).ToArray();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Solicitud cancelada por el cliente: {Method} {Path}", context.Request.Method, context.Request.Path);
            throw;
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogWarning(
                    ex,
                    "No se puede escribir ProblemDetails porque la respuesta HTTP ya comenzó en {Method} {Path}.",
                    context.Request.Method,
                    context.Request.Path);
                throw;
            }

            await EscribirProblemDetailsAsync(context, ex);
        }
    }

    private async Task EscribirProblemDetailsAsync(HttpContext context, Exception exception)
    {
        ProblemMapping mapping;
        if (TryMapException(exception, context, out var mapped))
        {
            mapping = mapped;
            _logger.LogWarning(
                exception,
                "Excepción controlada mapeada a {StatusCode} ({Title}) en {Method} {Path}.",
                mapping.StatusCode,
                mapping.Title,
                context.Request.Method,
                context.Request.Path);
        }
        else
        {
            mapping = new ProblemMapping(
                StatusCodes.Status500InternalServerError,
                "Error interno del servidor",
                "Ocurrió un error inesperado al procesar la solicitud.");

            _logger.LogError(
                exception,
                "Excepción no controlada procesando {Method} {Path}.",
                context.Request.Method,
                context.Request.Path);
        }

        var correlationId = ObtenerCorrelationId(context);

        var problemDetails = new ProblemDetails
        {
            Type = mapping.Type ?? ObtenerRfcType(mapping.StatusCode),
            Title = mapping.Title,
            Status = mapping.StatusCode,
            Detail = mapping.Detail,
            Instance = context.Request.Path
        };
        problemDetails.Extensions["correlationId"] = correlationId;

        context.Response.StatusCode = mapping.StatusCode;
        context.Response.ContentType = "application/problem+json";

        var problemDetailsService = context.RequestServices?.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = problemDetails,
                Exception = exception
            });
        }
        else
        {
            await context.Response.WriteAsJsonAsync(
                problemDetails,
                options: null,
                contentType: "application/problem+json");
        }
    }

    private bool TryMapException(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        for (var i = 0; i < _mappers.Count; i++)
        {
            if (_mappers[i].TryMap(exception, context, out mapping))
            {
                return true;
            }
        }

        mapping = default!;
        return false;
    }

    private static string ObtenerCorrelationId(HttpContext context)
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId;
        }

        if (context.Items.TryGetValue("CorrelationId", out var itemValue) &&
            itemValue is string itemStr &&
            !string.IsNullOrWhiteSpace(itemStr))
        {
            return itemStr;
        }

        return context.TraceIdentifier;
    }

    private static string ObtenerRfcType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status422UnprocessableEntity => "https://tools.ietf.org/html/rfc4918#section-11.2",
        _ => "https://tools.ietf.org/html/rfc9110#section-15.6.1"
    };
}
