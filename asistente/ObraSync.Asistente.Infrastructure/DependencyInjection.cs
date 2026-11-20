using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Application.Generadas;

namespace ObraSync.Asistente.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registra el cliente del modelo y los lectores de catálogo, obras, requisiciones, licitaciones,
    /// órdenes pendientes, partidas presupuestarias y órdenes bloqueadas (solo GET).
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

        // Lectura de remitos por foto (#209): un solo lector para todo el servicio (no depende del usuario).
        services.AddSingleton<IExtractorRemito>(sp => new LectorRemitoAzure(sp.GetRequiredService<ConfiguracionIA>()));

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

        services.AddScoped<IConsultorLicitaciones>(sp => new LicitacionesGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        services.AddScoped<IConsultorRecepciones>(sp => new RecepcionesGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        services.AddScoped<IConsultorPresupuestos>(sp => new PresupuestosGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        services.AddScoped<IConsultorOrdenesBloqueadas>(sp => new OrdenesBloqueadasGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        // Herramientas generadas desde contracts/openapi (#151): un solo cliente de lectura genérico.
        services.AddScoped<IClienteApiLectura>(sp => new ApiLecturaGateway(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("catalogo"),
            sp.GetRequiredService<ConfiguracionApi>(),
            sp.GetRequiredService<IContextoDePeticion>()));

        return services;
    }
}
