using System;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure.Messaging;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using ObraSync.Requisiciones.Infrastructure.Persistence.Repositories;
using ObraSync.Requisiciones.Infrastructure.Services;

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
                        cfg.UsarDecimalesComoNumero(); // decimales como número en los eventos (#128)
                        cfg.ConfigureEndpoints(context);
                    });
                }
                else
                {
                    var username = configuration["RabbitMQ:Username"];
                    var password = configuration["RabbitMQ:Password"];
                    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    {
                        throw new InvalidOperationException("RabbitMQ Username y Password deben estar configurados cuando no se utiliza el transporte en memoria (#169). Configure 'RabbitMQ:Username' y 'RabbitMQ:Password'.");
                    }

                    // Outbox transaccional (#134): el evento se guarda en DB_Req junto con el cambio de
                    // estado y un servicio en segundo plano lo entrega a RabbitMQ. Si RabbitMQ esta
                    // caido, el pedido no se cuelga ni se pierde el evento: se entrega al volver.
                    // Solo con el transporte real; con el transporte en memoria (tests) se publica directo.
                    x.AddEntityFrameworkOutbox<RequisicionesDbContext>(outbox =>
                    {
                        outbox.UseSqlServer();
                        outbox.UseBusOutbox();
                    });

                    x.UsingRabbitMq((context, cfg) =>
                    {
                        var host = configuration["RabbitMQ:Host"] ?? "localhost";
                        var virtualHost = configuration["RabbitMQ:VirtualHost"] ?? "/";

                        cfg.Host(host, virtualHost, h =>
                        {
                            h.Username(username);
                            h.Password(password);
                        });

                        cfg.UsarDecimalesComoNumero(); // decimales como número en los eventos (#128)
                        cfg.ConfigureEndpoints(context);
                    });
                }
            });

            // #178: la validación "la obra está asignada al Capataz" depende de Usuarios y Obras.
            // Antes, si faltaba la URL el validador dejaba pasar cualquier obra sin avisar.
            // Ahora el servicio no arranca sin esa URL; solo las pruebas aisladas (transporte en
            // memoria) usan un validador que deja pasar todo, y es explícito.
            var usuariosObrasBaseUrl = configuration["UsuariosObras:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(usuariosObrasBaseUrl))
            {
                services.AddScoped<IObraAsignacionValidator, HttpObraAsignacionValidator>();
            }
            else if (useInMemoryTransport)
            {
                services.AddScoped<IObraAsignacionValidator, ValidadorObraAsignacionDeshabilitado>();
            }
            else
            {
                throw new InvalidOperationException("UsuariosObras:BaseUrl debe estar configurado cuando no se utiliza el transporte en memoria (#178). Configure 'UsuariosObras:BaseUrl' (en Docker: http://usuarios-obras-api:8080).");
            }

            services.AddScoped<IRequisicionEventPublisher, MassTransitRequisicionEventPublisher>();

            return services;
        }
    }
}
