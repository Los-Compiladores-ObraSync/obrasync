using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Api.Middlewares;

public sealed class ValidacionExceptionMappingStrategy : ExceptionMappingStrategy<ValidacionException>
{
    protected override ExceptionProblemMapping Map(ValidacionException ex) => new(
        StatusCodes.Status400BadRequest,
        "Petición inválida",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.1");
}

public sealed class UnauthorizedExceptionMappingStrategy : ExceptionMappingStrategy<UnauthorizedAccessException>
{
    protected override ExceptionProblemMapping Map(UnauthorizedAccessException ex) => new(
        StatusCodes.Status401Unauthorized,
        "No autorizado",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.2");
}

public sealed class ForbiddenExceptionMappingStrategy : ExceptionMappingStrategy<ForbiddenException>
{
    protected override ExceptionProblemMapping Map(ForbiddenException ex) => new(
        StatusCodes.Status403Forbidden,
        "Acceso prohibido",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.4");
}

public sealed class KeyNotFoundExceptionMappingStrategy : ExceptionMappingStrategy<KeyNotFoundException>
{
    protected override ExceptionProblemMapping Map(KeyNotFoundException ex) => new(
        StatusCodes.Status404NotFound,
        "Recurso no encontrado",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.5");
}

public sealed class ConflictoExceptionMappingStrategy : ExceptionMappingStrategy<ConflictoException>
{
    protected override ExceptionProblemMapping Map(ConflictoException ex) => new(
        StatusCodes.Status409Conflict,
        "Conflicto en la operación",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.10");
}

public sealed class ServicioNoDisponibleExceptionMappingStrategy : ExceptionMappingStrategy<ServicioNoDisponibleException>
{
    protected override ExceptionProblemMapping Map(ServicioNoDisponibleException ex) => new(
        StatusCodes.Status503ServiceUnavailable,
        "Servicio no disponible",
        ex.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.6.4");
}

/// <summary>
/// Resuelve el mapeo de excepciones delegando en las estrategias registradas (OCP).
/// Solo las excepciones con estrategia explícita exponen su mensaje; el resto cae en un 500 genérico (#136).
/// </summary>
public sealed class ExceptionProblemDetailsMapper : IExceptionProblemDetailsMapper
{
    private static readonly ExceptionProblemMapping FallbackInternalServerError = new(
        StatusCodes.Status500InternalServerError,
        "Error interno del servidor",
        "Ocurrió un error inesperado en el servidor.",
        "https://tools.ietf.org/html/rfc9110#section-15.6.1");

    private readonly IReadOnlyList<IExceptionMappingStrategy> _strategies;

    public ExceptionProblemDetailsMapper(IEnumerable<IExceptionMappingStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies.ToList();
    }

    public static IExceptionProblemDetailsMapper CreateDefault() =>
        new ExceptionProblemDetailsMapper(GetDefaultStrategies());

    public static IReadOnlyList<IExceptionMappingStrategy> GetDefaultStrategies() =>
        new IExceptionMappingStrategy[]
        {
            new ValidacionExceptionMappingStrategy(),
            new UnauthorizedExceptionMappingStrategy(),
            new ForbiddenExceptionMappingStrategy(),
            new KeyNotFoundExceptionMappingStrategy(),
            new ConflictoExceptionMappingStrategy(),
            new ServicioNoDisponibleExceptionMappingStrategy()
        };

    public ExceptionProblemMapping Resolve(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var strategy = _strategies.FirstOrDefault(s => s.CanHandle(exception));
        return strategy is not null
            ? strategy.Map(exception)
            : FallbackInternalServerError;
    }

    public ProblemDetails CreateProblemDetails(HttpContext context, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(exception);

        var mapping = Resolve(exception);
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                            ?? context.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Type = mapping.TypeUri,
            Status = mapping.StatusCode,
            Title = mapping.Title,
            Detail = mapping.Detail,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;
        return problemDetails;
    }
}
