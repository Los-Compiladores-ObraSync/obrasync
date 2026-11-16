using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el cliente del modelo y los lectores de catálogo, obras y requisiciones (solo GET).
    /// Se arman a mano (con IHttpClientFactory) para que el buscador reciba el contexto del pedido actual
    /// (el JWT del usuario) y no uno viejo.
    /// </summary>
    public static IServiceCollection AddAsistenteInfrastructure(
        this IServiceCollection services, IConfiguration configuracion)
    {
        services.AddSingleton(ConfiguracionIA.Desde(configuracion));
        services.AddSingleton(ConfiguracionApi.Desde(configuracion));

        // Timeout corto hacia el modelo: el agente además tiene su propio tope de 45 s en total.
        services.AddHttpClient("modelo", cliente => cliente.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient("catalogo");

        services.AddScoped<IModeloChat>(sp => new ClienteGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("modelo"),
            sp.GetRequiredService<ConfiguracionIA>()));

        services.AddScoped<IBuscadorInsumos>(sp => new BuscadorGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        services.AddScoped<IBuscadorObras>(sp => new ObrasGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        services.AddScoped<IConsultorRequisiciones>(sp => new RequisicionesGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        return services;
    }
}
