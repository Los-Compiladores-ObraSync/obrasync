using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Licitaciones.Application.Exceptions;

namespace ObraSync.Licitaciones.Api.Middlewares;

public sealed class EndpointMetadataExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => -200;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        var attributes = context.GetEndpoint()?.Metadata.GetOrderedMetadata<MapExceptionProblemAttribute>();
        if (attributes is null || attributes.Count == 0)
        {
            mapping = default!;
            return false;
        }

        MapExceptionProblemAttribute? mejor = null;
        int mejorProfundidad = -1;

        for (var i = 0; i < attributes.Count; i++)
        {
            var attr = attributes[i];
            if (!attr.ExceptionType.IsInstanceOfType(exception))
            {
                continue;
            }

            var profundidad = CalcularProfundidadHerencia(attr.ExceptionType);
            // A igual profundidad gana el último atributo (el del método pisa al de la clase).
            if (profundidad >= mejorProfundidad)
            {
                mejor = attr;
                mejorProfundidad = profundidad;
            }
        }

        if (mejor is null)
        {
            mapping = default!;
            return false;
        }

        mapping = new ProblemMapping(mejor.StatusCode, mejor.Title, exception.Message);
        return true;
    }

    private static int CalcularProfundidadHerencia(Type type)
    {
        var depth = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }
}

public sealed class ApplicationExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 100;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        switch (exception)
        {
            case ValidacionException ex:
                mapping = new ProblemMapping(StatusCodes.Status400BadRequest, "Datos inválidos", ex.Message);
                return true;
            case NoEncontradoException ex:
                mapping = new ProblemMapping(StatusCodes.Status404NotFound, "No encontrado", ex.Message);
                return true;
            case ConflictoException ex:
                mapping = new ProblemMapping(StatusCodes.Status409Conflict, "Conflicto", ex.Message);
                return true;
            default:
                mapping = default!;
                return false;
        }
    }
}

public sealed class ConcurrencyExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 100;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            mapping = new ProblemMapping(
                StatusCodes.Status409Conflict,
                "Conflicto de concurrencia",
                "El recurso fue modificado por otro usuario en simultáneo. Por favor, actualice los datos e intente nuevamente.");
            return true;
        }

        mapping = default!;
        return false;
    }
}

public sealed class UnauthorizedExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 100;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        if (exception is UnauthorizedAccessException ex)
        {
            mapping = new ProblemMapping(StatusCodes.Status403Forbidden, "Acceso denegado", ex.Message);
            return true;
        }

        mapping = default!;
        return false;
    }
}

public sealed class KeyNotFoundExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 200;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        if (exception is KeyNotFoundException ex)
        {
            mapping = new ProblemMapping(StatusCodes.Status404NotFound, "Recurso no encontrado", ex.Message);
            return true;
        }

        mapping = default!;
        return false;
    }
}

public sealed class InvalidOperationExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 200;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        if (exception is InvalidOperationException ex)
        {
            mapping = new ProblemMapping(StatusCodes.Status409Conflict, "Conflicto", ex.Message);
            return true;
        }

        mapping = default!;
        return false;
    }
}

public sealed class ArgumentExceptionProblemMapper : IExceptionProblemMapper
{
    public int Order => 200;

    public bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping)
    {
        if (exception is ArgumentException ex)
        {
            mapping = new ProblemMapping(StatusCodes.Status400BadRequest, "Solicitud inválida", ex.Message);
            return true;
        }

        mapping = default!;
        return false;
    }
}

public static class ExceptionProblemMappingServiceCollectionExtensions
{
    public static IServiceCollection AddExceptionProblemMapping(this IServiceCollection services)
    {
        services.AddSingleton<IExceptionProblemMapper, EndpointMetadataExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, ApplicationExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, ConcurrencyExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, UnauthorizedExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, KeyNotFoundExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, InvalidOperationExceptionProblemMapper>();
        services.AddSingleton<IExceptionProblemMapper, ArgumentExceptionProblemMapper>();
        return services;
    }
}
