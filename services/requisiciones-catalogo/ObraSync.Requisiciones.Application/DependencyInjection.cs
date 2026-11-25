using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Queries;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries;

namespace ObraSync.Requisiciones.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            // Registramos los Handlers de Casos de Uso (CQRS) tanto por abstracción (DIP) como por tipo concreto
            services.AddScoped<GetInsumosQueryHandler>();
            services.AddScoped<IQueryHandler<GetInsumosQuery, PaginaDe<InsumoCatalogoDto>>>(
                sp => sp.GetRequiredService<GetInsumosQueryHandler>());

            services.AddScoped<CreateInsumoCommandHandler>();
            services.AddScoped<ICommandHandler<CreateInsumoCommand, InsumoCatalogoDto>>(
                sp => sp.GetRequiredService<CreateInsumoCommandHandler>());

            services.AddScoped<CreateRequisicionCommandHandler>();
            services.AddScoped<ICommandHandler<CreateRequisicionCommand, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<CreateRequisicionCommandHandler>());

            services.AddScoped<GetRequisicionesQueryHandler>();
            services.AddScoped<IQueryHandler<GetRequisicionesQuery, PaginaDe<RequisicionResponseDto>>>(
                sp => sp.GetRequiredService<GetRequisicionesQueryHandler>());

            services.AddScoped<GetRequisicionByIdQueryHandler>();
            services.AddScoped<IQueryHandler<GetRequisicionByIdQuery, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<GetRequisicionByIdQueryHandler>());

            services.AddScoped<AprobarRequisicionCommandHandler>();
            services.AddScoped<ICommandHandler<AprobarRequisicionCommand, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<AprobarRequisicionCommandHandler>());

            services.AddScoped<RechazarRequisicionCommandHandler>();
            services.AddScoped<ICommandHandler<RechazarRequisicionCommand, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<RechazarRequisicionCommandHandler>());

            services.AddScoped<ReenviarRequisicionCommandHandler>();
            services.AddScoped<ICommandHandler<ReenviarRequisicionCommand, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<ReenviarRequisicionCommandHandler>());

            services.AddScoped<CancelarRequisicionCommandHandler>();
            services.AddScoped<ICommandHandler<CancelarRequisicionCommand, RequisicionResponseDto>>(
                sp => sp.GetRequiredService<CancelarRequisicionCommandHandler>());

            return services;
        }
    }
}
