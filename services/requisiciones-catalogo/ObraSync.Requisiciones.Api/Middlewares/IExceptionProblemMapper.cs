using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Requisiciones.Api.Exceptions;
using ObraSync.Requisiciones.Application.Exceptions;

namespace ObraSync.Requisiciones.Api.Middlewares;

public interface IExceptionProblemMapper
{
    bool CanHandle(Exception exception);
    ProblemDetails Map(Exception exception);
}

public sealed class DelegateExceptionProblemMapper<TException> : IExceptionProblemMapper
    where TException : Exception
{
    private readonly Func<TException, ProblemDetails> _map;

    public DelegateExceptionProblemMapper(Func<TException, ProblemDetails> map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    public bool CanHandle(Exception exception) => exception is TException;

    public ProblemDetails Map(Exception exception) => _map((TException)exception);
}

public static class DefaultExceptionProblemMappers
{
    public static IReadOnlyList<IExceptionProblemMapper> Create() =>
    [
        new DelegateExceptionProblemMapper<RequisicionDuplicadaException>(ex =>
        {
            var pd = Crear(
                StatusCodes.Status409Conflict,
                "Requisición duplicada",
                ex.Message,
                "https://tools.ietf.org/html/rfc9110#section-15.5.10");
            pd.Extensions["numeroRequisicion"] = ex.NumeroRequisicionExistente;
            pd.Extensions["requisicionId"] = ex.RequisicionExistenteId;
            return pd;
        }),
        new DelegateExceptionProblemMapper<DbUpdateConcurrencyException>(_ => Crear(
            StatusCodes.Status409Conflict,
            "Conflicto de concurrencia en la requisición",
            "La requisición cambió, recargá e intentá nuevamente.",
            "https://tools.ietf.org/html/rfc9110#section-15.5.10")),
        new DelegateExceptionProblemMapper<ObraAsignacionNoVerificableException>(_ => Crear(
            StatusCodes.Status503ServiceUnavailable,
            "No se pudo verificar la obra",
            "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
            "https://tools.ietf.org/html/rfc9110#section-15.6.4")),
        new DelegateExceptionProblemMapper<ObraNoAsignadaException>(ex => Crear(
            StatusCodes.Status403Forbidden,
            "Obra no asignada",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.4")),
        new DelegateExceptionProblemMapper<UsuarioNoIdentificadoException>(ex => Crear(
            StatusCodes.Status401Unauthorized,
            "Usuario no identificado",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.2")),
        new DelegateExceptionProblemMapper<PaginacionInvalidaException>(ex => Crear(
            StatusCodes.Status400BadRequest,
            "Paginación inválida",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1")),
        new DelegateExceptionProblemMapper<InvalidOperationException>(ex => Crear(
            StatusCodes.Status409Conflict,
            "Conflicto en la operación",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.10")),
        new DelegateExceptionProblemMapper<ArticuloCatalogoNoEncontradoException>(ex => Crear(
            StatusCodes.Status404NotFound,
            "Artículo de catálogo inexistente",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.5")),
        new DelegateExceptionProblemMapper<KeyNotFoundException>(ex => Crear(
            StatusCodes.Status404NotFound,
            ex.Message.Contains("artículo", StringComparison.OrdinalIgnoreCase)
                ? "Artículo de catálogo inexistente"
                : "Requisición no encontrada",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.5")),
        new DelegateExceptionProblemMapper<UnauthorizedAccessException>(ex => Crear(
            StatusCodes.Status403Forbidden,
            "Acceso denegado: no es el solicitante dueño de la requisición",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.4")),
        // ArgumentNullException deriva de ArgumentException: si ocurre por un bug interno debe devolver 500, no 400.
        new DelegateExceptionProblemMapper<ArgumentNullException>(_ => CrearErrorInterno()),
        new DelegateExceptionProblemMapper<ArgumentException>(ex => Crear(
            StatusCodes.Status400BadRequest,
            "Datos inválidos",
            ex.Message,
            "https://tools.ietf.org/html/rfc9110#section-15.5.1"))
    ];

    public static ProblemDetails CrearErrorInterno() => Crear(
        StatusCodes.Status500InternalServerError,
        "Error interno del servidor",
        "Ocurrió un error inesperado en el servidor.",
        "https://tools.ietf.org/html/rfc9110#section-15.6.1");

    private static ProblemDetails Crear(int statusCode, string title, string detail, string type) => new()
    {
        Type = type,
        Status = statusCode,
        Title = title,
        Detail = detail
    };
}
