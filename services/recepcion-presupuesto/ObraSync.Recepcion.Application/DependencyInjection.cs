using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Partidas.Commands;
using ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Validators;

namespace ObraSync.Recepcion.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<ICrearRecepcionCommandValidator, CrearRecepcionCommandValidator>();

        services.AddScoped<CrearRecepcionCommandHandler>(sp => new CrearRecepcionCommandHandler(
            sp.GetRequiredService<IOrdenCompraRepository>(),
            sp.GetRequiredService<IRecepcionRepository>(),
            sp.GetRequiredService<IPartidaPresupuestariaRepository>(),
            sp.GetRequiredService<IUnitOfWork>(),
            sp.GetRequiredService<IRecepcionEventPublisher>(),
            sp.GetRequiredService<ILogger<CrearRecepcionCommandHandler>>(),
            sp.GetService<ICrearRecepcionCommandValidator>()));
        services.AddScoped<ICommandHandler<CrearRecepcionCommand, RecepcionDto>>(
            sp => sp.GetRequiredService<CrearRecepcionCommandHandler>());

        services.AddScoped<ObtenerRecepcionesQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerRecepcionesQuery, PaginaDe<RecepcionDto>>>(
            sp => sp.GetRequiredService<ObtenerRecepcionesQueryHandler>());

        services.AddScoped<ObtenerRecepcionPorIdQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerRecepcionPorIdQuery, RecepcionDto?>>(
            sp => sp.GetRequiredService<ObtenerRecepcionPorIdQueryHandler>());

        services.AddScoped<ObtenerRecepcionesPorOrdenQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerRecepcionesPorOrdenQuery, RecepcionesDeOrdenDto?>>(
            sp => sp.GetRequiredService<ObtenerRecepcionesPorOrdenQueryHandler>());

        services.AddScoped<ObtenerFotoRecepcionQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerFotoRecepcionQuery, FotoDescargaDto?>>(
            sp => sp.GetRequiredService<ObtenerFotoRecepcionQueryHandler>());

        services.AddScoped<ObtenerOrdenesPendientesQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerOrdenesPendientesQuery, PaginaDe<OrdenPendienteDto>>>(
            sp => sp.GetRequiredService<ObtenerOrdenesPendientesQueryHandler>());

        services.AddScoped<ObtenerOrdenesBloqueadasQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerOrdenesBloqueadasQuery, PaginaDe<OrdenBloqueadaDto>>>(
            sp => sp.GetRequiredService<ObtenerOrdenesBloqueadasQueryHandler>());

        services.AddScoped<LiberarOrdenCompraCommandHandler>(sp => new LiberarOrdenCompraCommandHandler(
            sp.GetRequiredService<IOrdenCompraRepository>(),
            sp.GetRequiredService<IPartidaPresupuestariaRepository>(),
            sp.GetRequiredService<IUnitOfWork>()));
        services.AddScoped<ICommandHandler<LiberarOrdenCompraCommand, OrdenCompraLiberadaDto>>(
            sp => sp.GetRequiredService<LiberarOrdenCompraCommandHandler>());

        services.AddScoped<LiquidarOrdenCompraCommandHandler>(sp => new LiquidarOrdenCompraCommandHandler(
            sp.GetRequiredService<IOrdenCompraRepository>(),
            sp.GetRequiredService<IRecepcionRepository>(),
            sp.GetRequiredService<IUnitOfWork>()));
        services.AddScoped<ICommandHandler<LiquidarOrdenCompraCommand, OrdenCompraLiquidadaDto>>(
            sp => sp.GetRequiredService<LiquidarOrdenCompraCommandHandler>());

        // Partidas presupuestarias (#93, #154)
        services.AddScoped<CrearPartidaPresupuestariaCommandHandler>(sp => new CrearPartidaPresupuestariaCommandHandler(
            sp.GetRequiredService<IPartidaPresupuestariaRepository>(),
            sp.GetRequiredService<IUnitOfWork>()));
        services.AddScoped<ICommandHandler<CrearPartidaPresupuestariaCommand, PartidaPresupuestariaDto>>(
            sp => sp.GetRequiredService<CrearPartidaPresupuestariaCommandHandler>());

        services.AddScoped<ObtenerPartidasQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerPartidasQuery, PaginaDe<PartidaPresupuestariaDto>>>(
            sp => sp.GetRequiredService<ObtenerPartidasQueryHandler>());

        services.AddScoped<ObtenerPartidaPorIdQueryHandler>();
        services.AddScoped<IQueryHandler<ObtenerPartidaPorIdQuery, PartidaPresupuestariaDto?>>(
            sp => sp.GetRequiredService<ObtenerPartidaPorIdQueryHandler>());

        return services;
    }
}
