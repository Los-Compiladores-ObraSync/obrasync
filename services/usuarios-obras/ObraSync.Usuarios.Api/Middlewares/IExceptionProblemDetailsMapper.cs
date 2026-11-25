using Microsoft.AspNetCore.Mvc;

namespace ObraSync.Usuarios.Api.Middlewares;

/// <summary>
/// Servicio responsable de construir instancias de <see cref="ProblemDetails"/> a partir de excepciones.
/// </summary>
public interface IExceptionProblemDetailsMapper
{
    ExceptionProblemMapping Resolve(Exception exception);
    ProblemDetails CreateProblemDetails(HttpContext context, Exception exception);
}
