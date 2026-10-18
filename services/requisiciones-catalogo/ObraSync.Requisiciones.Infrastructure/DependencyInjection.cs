using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure.Persistence;

namespace ObraSync.Requisiciones.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? configuration.GetConnectionString("DbReq")
                ?? "Server=localhost,1433;Database=DB_Req;User Id=sa;Password=PasswordSeguro123!;TrustServerCertificate=True;";

            services.AddDbContext<RequisicionesDbContext>(options =>
                options.UseSqlServer(connectionString, b => b.MigrationsAssembly(typeof(RequisicionesDbContext).Assembly.FullName)));

            services.AddScoped<IRequisicionesDbContext>(provider => provider.GetRequiredService<RequisicionesDbContext>());

            return services;
        }
    }
}
