using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Api.Middlewares.ExceptionMapping;

namespace ObraSync.Recepcion.Api.Middlewares;

/// <summary>
/// Middleware centralizado de excepciones para Recepción y Control Presupuestario (#94, #136).
/// Aplica el principio Abierto/Cerrado (OCP) delegando la traducción a estrategias <see cref="IExceptionProblemMapper"/>,
/// formateando las respuestas como RFC 7807 (ProblemDetails) con propagación obligatoria de CorrelationId.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IReadOnlyList<IExceptionProblemMapper> _mappers;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IEnumerable<IExceptionProblemMapper>? mappers = null)
    {
        _next = next;
        _logger = logger;

        var configurados = mappers?.ToList();
        var origen = configurados is { Count: > 0 }
            ? configurados
            : DefaultExceptionProblemMappers.CreateAll();

        _mappers = origen.OrderBy(m => m.Order).ToList();
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
        var mapping = ResolverMapeo(exception);

        if (mapping.StatusCode == StatusCodes.Status500InternalServerError)
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
        context.Response.StatusCode = mapping.StatusCode;

        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? context.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Type = mapping.Type,
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }

    private ExceptionProblemMapping ResolverMapeo(Exception exception)
    {
        foreach (var mapper in _mappers)
        {
            if (mapper.CanMap(exception))
            {
                return mapper.Map(exception);
            }
        }

        return DefaultExceptionProblemMappers.FallbackInternalServerError;
    }
}
