using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Recepcion.Application;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Persistence.Repositories;
using ObraSync.Recepcion.Infrastructure.Services;

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
        services.AddScoped<IOrdenCompraRepository, OrdenCompraRepository>();
        services.AddScoped<IRecepcionEventPublisher, Messaging.MassTransitRecepcionEventPublisher>();

        // Resiliencia con Polly v8 para llamadas HTTP salientes (#103)
        var resilienciaOptions = configuration
            .GetSection(ResilienciaHttpOptions.SectionName)
            .Get<ResilienciaHttpOptions>() ?? new ResilienciaHttpOptions();

        services.AddSingleton(resilienciaOptions);
        services.AddSingleton(sp => PipelineResilienciaHttp.Crear(sp.GetRequiredService<ResilienciaHttpOptions>()));

        // Validación de pertenencia de obra para Capataz (#189, #155, #178)
        var usuariosObrasBaseUrl = configuration["UsuariosObras:BaseUrl"];

        if (!string.IsNullOrWhiteSpace(usuariosObrasBaseUrl))
        {
            services.AddScoped<IObraAsignacionValidator, HttpObraAsignacionValidator>();
        }
        else
        {
            services.AddScoped<IObraAsignacionValidator, ValidadorObraAsignacionDeshabilitado>();
        }

        services.AddApplicationServices();

        return services;
    }
}
