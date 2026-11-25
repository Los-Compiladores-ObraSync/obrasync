using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ObraSync.Requisiciones.Api.Middlewares;

/// <summary>
/// Middleware centralizado de excepciones para Requisiciones y Catálogo.
/// Traduce excepciones de dominio, aplicación y concurrencia a respuestas RFC 7807 (ProblemDetails)
/// mediante mapeadores extensibles (<see cref="IExceptionProblemMapper"/>) y propaga CorrelationId.
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
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var lista = mappers?.ToList();
        _mappers = lista is { Count: > 0 } ? lista : DefaultExceptionProblemMappers.Create();
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
        var mapper = _mappers.FirstOrDefault(m => m.CanHandle(exception));
        var problemDetails = mapper is not null
            ? mapper.Map(exception)
            : DefaultExceptionProblemMappers.CrearErrorInterno();

        var statusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;

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

        problemDetails.Instance = context.Request.Path;
        problemDetails.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problemDetails, options: null, contentType: "application/problem+json");
    }
}
