using System;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure.Messaging;
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
            services.AddScoped<IRequisicionRepository, RequisicionRepository>();

            var useInMemoryTransport = string.Equals(
                configuration["RabbitMQ:UseInMemory"],
                "true",
                StringComparison.OrdinalIgnoreCase);

            services.AddMassTransit(x =>
            {
                if (useInMemoryTransport)
                {
                    x.UsingInMemory((context, cfg) =>
                    {
                        cfg.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var host = configuration["RabbitMQ:Host"] ?? "localhost";
                        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";
                        var username = configuration["RabbitMQ:Username"] ?? "guest";
                        var password = configuration["RabbitMQ:Password"] ?? "guest";

                        cfg.Host(host, virtualHost, h =>
                        {
                            h.Username(username);
                            h.Password(password);
                        });

                        cfg.ConfigureEndpoints(context);
                    });
                }
            });

            services.AddScoped<IRequisicionEventPublisher, MassTransitRequisicionEventPublisher>();

            return services;
        }
    }
}
