namespace ObraSync.Recepcion.Api.Middlewares.ExceptionMapping;

/// <summary>
/// Estrategia extensible de mapeo de excepciones a ProblemDetails (OCP).
/// Nuevos tipos de excepción pueden registrarse en DI sin modificar <see cref="ExceptionMiddleware"/>.
/// </summary>
public interface IExceptionProblemMapper
{
    /// <summary>
    /// Prioridad de evaluación (menor valor se evalúa primero). Permite que mapeadores específicos
    /// tengan precedencia sobre mapeadores de tipos base.
    /// </summary>
    int Order => 0;

    bool CanMap(Exception exception);

    ExceptionProblemMapping Map(Exception exception);
}
