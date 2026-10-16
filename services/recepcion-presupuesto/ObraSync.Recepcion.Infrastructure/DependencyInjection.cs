using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetConnectionString("DbRecepcion")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'DefaultConnection' o 'DbRecepcion'.");

        services.AddDbContext<RecepcionDbContext>(options =>
            options.UseSqlServer(connectionString, b => b.MigrationsAssembly(typeof(RecepcionDbContext).Assembly.FullName)));

        services.AddScoped<IRecepcionDbContext>(provider => provider.GetRequiredService<RecepcionDbContext>());

        return services;
    }
}
