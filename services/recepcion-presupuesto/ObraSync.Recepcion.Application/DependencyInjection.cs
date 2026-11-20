using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;
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
        services.AddScoped<ObtenerOrdenesBloqueadasQueryHandler>();
        services.AddScoped<LiberarOrdenCompraCommandHandler>();
        services.AddScoped<LiquidarOrdenCompraCommandHandler>();

        // Partidas presupuestarias (#93, #154)
        services.AddScoped<CrearPartidaPresupuestariaCommandHandler>();
        services.AddScoped<ObtenerPartidasQueryHandler>();
        services.AddScoped<ObtenerPartidaPorIdQueryHandler>();

        return services;
    }
}
