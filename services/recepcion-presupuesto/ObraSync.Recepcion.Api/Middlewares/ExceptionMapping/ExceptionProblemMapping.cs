namespace ObraSync.Recepcion.Api.Middlewares.ExceptionMapping;

/// <summary>
/// Representa el resultado de proyectar una excepción a los campos estándar RFC 7807 (ProblemDetails).
/// </summary>
public readonly record struct ExceptionProblemMapping(
    int StatusCode,
    string Title,
    string Detail,
    string Type);
