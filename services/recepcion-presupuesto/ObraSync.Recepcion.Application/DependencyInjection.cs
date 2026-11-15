using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

namespace ObraSync.Recepcion.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<CrearRecepcionCommandHandler>();
        services.AddScoped<ObtenerRecepcionesQueryHandler>();
        services.AddScoped<ObtenerRecepcionPorIdQueryHandler>();
        services.AddScoped<ObtenerOrdenesPendientesQueryHandler>();

        return services;
    }
}
