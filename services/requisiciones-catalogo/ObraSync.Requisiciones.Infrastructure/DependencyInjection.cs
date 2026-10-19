using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using ObraSync.Requisiciones.Infrastructure.Persistence.Repositories;

namespace ObraSync.Requisiciones.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? configuration.GetConnectionString("DbReq");

            services.AddDbContext<RequisicionesDbContext>(options =>
                options.UseSqlServer(connectionString, b => b.MigrationsAssembly(typeof(RequisicionesDbContext).Assembly.FullName)));

            services.AddScoped<IInsumoRepository, InsumoRepository>();

            return services;
        }
    }
}
