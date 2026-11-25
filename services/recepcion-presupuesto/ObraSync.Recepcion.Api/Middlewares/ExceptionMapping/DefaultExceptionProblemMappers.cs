using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Domain.Exceptions;

namespace ObraSync.Recepcion.Api.Middlewares.ExceptionMapping;

public sealed class RecepcionConflictoExceptionMapper : IExceptionProblemMapper
{
    public bool CanMap(Exception exception) => exception is RecepcionConflictoException;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status409Conflict,
        "Conflicto en la operación",
        exception.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.10");
}

public sealed class TransicionEstadoInvalidaExceptionMapper : IExceptionProblemMapper
{
    public bool CanMap(Exception exception) => exception is TransicionEstadoInvalidaException;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status409Conflict,
        "Transición de estado inválida",
        exception.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.10");
}

public sealed class ArgumentExceptionMapper : IExceptionProblemMapper
{
    public int Order => 10;

    public bool CanMap(Exception exception) => exception is ArgumentException;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status400BadRequest,
        "Petición inválida",
        exception.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.1");
}

public sealed class KeyNotFoundExceptionMapper : IExceptionProblemMapper
{
    public bool CanMap(Exception exception) => exception is KeyNotFoundException;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status404NotFound,
        "Recurso no encontrado",
        exception.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.5");
}

public sealed class UnauthorizedAccessExceptionMapper : IExceptionProblemMapper
{
    public bool CanMap(Exception exception) => exception is UnauthorizedAccessException;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status401Unauthorized,
        "No autorizado",
        exception.Message,
        "https://tools.ietf.org/html/rfc9110#section-15.5.2");
}

public sealed class PayloadTooLargeBadHttpRequestExceptionMapper : IExceptionProblemMapper
{
    public int Order => -10;

    public bool CanMap(Exception exception) =>
        exception is BadHttpRequestException ex && ex.StatusCode == StatusCodes.Status413PayloadTooLarge;

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status413PayloadTooLarge,
        "Solicitud demasiado grande",
        "El tamaño total del pedido supera el límite permitido.",
        "https://tools.ietf.org/html/rfc9110#section-15.5.14");
}

public sealed class MultipartBodyLengthLimitExceptionMapper : IExceptionProblemMapper
{
    public int Order => -10;

    public bool CanMap(Exception exception) =>
        exception is InvalidDataException ex &&
        ex.Message.Contains("Multipart body length limit", StringComparison.OrdinalIgnoreCase);

    public ExceptionProblemMapping Map(Exception exception) => new(
        StatusCodes.Status413PayloadTooLarge,
        "Solicitud demasiado grande",
        "El tamaño total de los archivos adjuntos supera el límite permitido.",
        "https://tools.ietf.org/html/rfc9110#section-15.5.14");
}

public sealed class BadHttpRequestExceptionMapper : IExceptionProblemMapper
{
    public int Order => 20;

    public bool CanMap(Exception exception) => exception is BadHttpRequestException;

    public ExceptionProblemMapping Map(Exception exception)
    {
        var ex = (BadHttpRequestException)exception;
        return new ExceptionProblemMapping(
            ex.StatusCode,
            "Petición inválida",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1");
    }
}

public static class DefaultExceptionProblemMappers
{
    public static readonly ExceptionProblemMapping FallbackInternalServerError = new(
        StatusCodes.Status500InternalServerError,
        "Error interno del servidor",
        "Ocurrió un error inesperado en el servidor.",
        "https://tools.ietf.org/html/rfc9110#section-15.6.1");

    public static IReadOnlyList<IExceptionProblemMapper> CreateAll() =>
    [
        new PayloadTooLargeBadHttpRequestExceptionMapper(),
        new MultipartBodyLengthLimitExceptionMapper(),
        new RecepcionConflictoExceptionMapper(),
        new TransicionEstadoInvalidaExceptionMapper(),
        new KeyNotFoundExceptionMapper(),
        new UnauthorizedAccessExceptionMapper(),
        new ArgumentExceptionMapper(),
        new BadHttpRequestExceptionMapper()
    ];

    public static IServiceCollection AddExceptionProblemMappers(this IServiceCollection services)
    {
        services.AddSingleton<IExceptionProblemMapper, PayloadTooLargeBadHttpRequestExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, MultipartBodyLengthLimitExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, RecepcionConflictoExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, TransicionEstadoInvalidaExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, KeyNotFoundExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, UnauthorizedAccessExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, ArgumentExceptionMapper>();
        services.AddSingleton<IExceptionProblemMapper, BadHttpRequestExceptionMapper>();
        return services;
    }
}
