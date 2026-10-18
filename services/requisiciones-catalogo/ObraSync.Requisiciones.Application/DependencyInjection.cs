using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;

namespace ObraSync.Requisiciones.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Registramos los Handlers de Casos de Uso (CQRS sin MediatR)
            services.AddScoped<GetInsumosQueryHandler>();
            services.AddScoped<CreateInsumoCommandHandler>();

            return services;
        }
    }
}
