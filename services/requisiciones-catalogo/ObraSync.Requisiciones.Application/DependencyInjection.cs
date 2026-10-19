using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;

namespace ObraSync.Requisiciones.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Registramos los Handlers de Casos de Uso (CQRS sin MediatR)
            services.AddScoped<GetInsumosQueryHandler>();
            services.AddScoped<CreateInsumoCommandHandler>();

            services.AddScoped<CreateRequisicionCommandHandler>();
            services.AddScoped<GetRequisicionesQueryHandler>();
            services.AddScoped<GetRequisicionByIdQueryHandler>();
            services.AddScoped<AprobarRequisicionCommandHandler>();

            return services;
        }
    }
}
