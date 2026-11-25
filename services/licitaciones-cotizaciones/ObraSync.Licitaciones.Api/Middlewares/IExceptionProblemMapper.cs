using Microsoft.AspNetCore.Http;

namespace ObraSync.Licitaciones.Api.Middlewares;

public sealed record ProblemMapping(
    int StatusCode,
    string Title,
    string Detail,
    string? Type = null);

public interface IExceptionProblemMapper
{
    int Order => 0;

    bool TryMap(Exception exception, HttpContext context, out ProblemMapping mapping);
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class MapExceptionProblemAttribute : Attribute
{
    public MapExceptionProblemAttribute(Type exceptionType, int statusCode, string title)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        if (!typeof(Exception).IsAssignableFrom(exceptionType))
        {
            throw new ArgumentException(
                $"El tipo '{exceptionType.FullName}' debe heredar de System.Exception.",
                nameof(exceptionType));
        }

        ExceptionType = exceptionType;
        StatusCode = statusCode;
        Title = title;
    }

    public Type ExceptionType { get; }
    public int StatusCode { get; }
    public string Title { get; }
}
