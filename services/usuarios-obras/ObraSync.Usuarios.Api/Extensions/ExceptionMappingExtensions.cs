using ObraSync.Usuarios.Api.Middlewares;

namespace ObraSync.Usuarios.Api.Extensions;

public static class ExceptionMappingExtensions
{
    public static IServiceCollection AddExceptionProblemDetailsMapping(this IServiceCollection services)
    {
        services.AddSingleton<IExceptionMappingStrategy, ValidacionExceptionMappingStrategy>();
        services.AddSingleton<IExceptionMappingStrategy, UnauthorizedExceptionMappingStrategy>();
        services.AddSingleton<IExceptionMappingStrategy, ForbiddenExceptionMappingStrategy>();
        services.AddSingleton<IExceptionMappingStrategy, KeyNotFoundExceptionMappingStrategy>();
        services.AddSingleton<IExceptionMappingStrategy, ConflictoExceptionMappingStrategy>();
        services.AddSingleton<IExceptionMappingStrategy, ServicioNoDisponibleExceptionMappingStrategy>();
        services.AddSingleton<IExceptionProblemDetailsMapper, ExceptionProblemDetailsMapper>();
        return services;
    }
}
