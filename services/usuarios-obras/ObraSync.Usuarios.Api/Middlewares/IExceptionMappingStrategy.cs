namespace ObraSync.Usuarios.Api.Middlewares;

/// <summary>
/// Descriptor inmutable de la respuesta RFC 7807 / RFC 9110 asociada a una excepción.
/// </summary>
public sealed record ExceptionProblemMapping(
    int StatusCode,
    string Title,
    string Detail,
    string TypeUri);

/// <summary>
/// Estrategia abierta a extensión (OCP) para traducir un tipo específico de excepción a ProblemDetails.
/// </summary>
public interface IExceptionMappingStrategy
{
    bool CanHandle(Exception exception);
    ExceptionProblemMapping Map(Exception exception);
}

/// <summary>
/// Clase base tipada para registrar reglas de mapeo por tipo de excepción sin modificar ExceptionMiddleware.
/// </summary>
public abstract class ExceptionMappingStrategy<TException> : IExceptionMappingStrategy
    where TException : Exception
{
    public virtual bool CanHandle(Exception exception) => exception is TException;

    public ExceptionProblemMapping Map(Exception exception) => Map((TException)exception);

    protected abstract ExceptionProblemMapping Map(TException exception);
}
