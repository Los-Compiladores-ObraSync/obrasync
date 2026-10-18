using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Services;

namespace ObraSync.Requisiciones.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddScoped<ICatalogoService, CatalogoService>();
            return services;
        }
    }
}
