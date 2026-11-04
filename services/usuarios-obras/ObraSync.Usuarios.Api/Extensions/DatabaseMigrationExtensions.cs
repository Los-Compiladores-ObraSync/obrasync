using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    public static async Task SeedDatabaseAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        await SeedDatabaseAsync(scope.ServiceProvider);
    }

    public static async Task SeedDatabaseAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<AuthDbContext>>();
        var context = services.GetRequiredService<AuthDbContext>();

        // 1. Aplica migraciones pendientes si la base de datos SQL Server recién arranca en Docker
        if (context.Database.IsSqlServer())
        {
            await context.Database.MigrateAsync();
        }
        else if (context.Database.ProviderName?.Contains("InMemory") == true)
        {
            await context.Database.EnsureCreatedAsync();
        }

        var roleManager = services.GetRequiredService<RoleManager<AppRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        // 2. Garantiza que existan los 4 roles del sistema
        var roles = new[] { UserRoles.Capataz, UserRoles.Comprador, UserRoles.Proveedor, UserRoles.Admin };

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new AppRole(roleName));
                logger.LogInformation("Rol de sistema creado: {RoleName}", roleName);
            }
        }

        // 3. Sembrado de Usuarios de Prueba para la Demo (Tarjeta #30)
        const string defaultPassword = "Password123!";

        var demoUsers = new (string Email, string NombreCompleto, string Rol)[]
        {
            ("admin@obrasync.com", "Admin Sistema", UserRoles.Admin),
            ("capataz@obrasync.com", "Carlos Capataz", UserRoles.Capataz),
            ("comprador@obrasync.com", "Claudia Compradora", UserRoles.Comprador),
            ("proveedor@obrasync.com", "Pedro Proveedor", UserRoles.Proveedor)
        };

        foreach (var (email, nombreCompleto, rol) in demoUsers)
        {
            if (await userManager.FindByEmailAsync(email) is null)
            {
                var user = new AppUser
                {
                    UserName = email,
                    Email = email,
                    NombreCompleto = nombreCompleto,
                    Activo = true,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user, defaultPassword);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, rol);
                    logger.LogInformation("Usuario de prueba creado: {Email} con rol {Rol}", email, rol);
                }
                else
                {
                    logger.LogWarning("No se pudo crear el usuario {Email}: {Errors}",
                        email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                }
            }
        }

        // 4. Sembrado de Obras y Etapas de Ejemplo para la Demo (Tarjeta #60)
        // NOTA: Mismo alcance y mecanismo que los usuarios de prueba. Antes de cualquier
        // despliegue real, se decide si el sembrado se limita a Development.
        var capatazUser = await userManager.FindByEmailAsync("capataz@obrasync.com");
        if (capatazUser != null)
        {
            var demoObras = new (string Nombre, string Ubicacion, DateTime FechaInicio, string[] Etapas)[]
            {
                (
                    "Torre Alvear",
                    "Av. Alvear 1850, CABA",
                    new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
                    new[] { "Cimientos", "Estructura", "Mampostería", "Terminaciones" }
                ),
                (
                    "Residencial Parque",
                    "Av. del Libertador 4500, CABA",
                    new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
                    new[] { "Cimientos", "Estructura", "Instalaciones" }
                ),
                (
                    "Complejo Industrial Sur",
                    "Ruta 2 Km 45, Berazategui",
                    new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc),
                    new[] { "Movimiento de Suelos", "Fundaciones", "Estructura Metálica", "Cerramientos" }
                )
            };

            foreach (var (nombre, ubicacion, fechaInicio, etapas) in demoObras)
            {
                // Idempotente: antes de crear, comprobar por nombre que la obra no exista
                var existeObra = await context.Obras.AnyAsync(o => o.Nombre == nombre);
                if (!existeObra)
                {
                    var obra = new Obra
                    {
                        Id = Guid.NewGuid(),
                        Nombre = nombre,
                        Ubicacion = ubicacion,
                        FechaInicio = fechaInicio,
                        CapatazId = capatazUser.Id
                    };

                    foreach (var nombreEtapa in etapas)
                    {
                        obra.Etapas.Add(new EtapaObra
                        {
                            Id = Guid.NewGuid(),
                            NombreEtapa = nombreEtapa,
                            Estado = "Pendiente"
                        });
                    }

                    context.Obras.Add(obra);
                    logger.LogInformation("Obra de prueba creada: {Nombre} con {EtapasCount} etapas para capataz {CapatazEmail}",
                        nombre, etapas.Length, capatazUser.Email);
                }
            }

            await context.SaveChangesAsync();
        }
        else
        {
            logger.LogWarning("No se encontró el usuario 'capataz@obrasync.com'. No se sembraron las obras de ejemplo.");
        }
        // 5. Sembrado de Plantillas de Obra de Ejemplo (Tarjeta #114)
        var demoPlantillas = new (string Nombre, string Descripcion, string[] Etapas)[]
        {
            (
                "Edificio en Altura",
                "Plantilla estándar para desarrollo inmobiliario de múltiples pisos.",
                new[] { "Cimientos y Subsuelo", "Estructura de Hormigón", "Mampostería", "Instalaciones", "Terminaciones" }
            ),
            (
                "Vivienda Unifamiliar",
                "Plantilla básica para casas residenciales de una o dos plantas.",
                new[] { "Replanteo y Nivelación", "Fundaciones", "Muros y Cubierta", "Instalaciones y Yesería", "Terminaciones" }
            ),
            (
                "Nave Industrial",
                "Plantilla para galpones, centros de distribución y depósitos logísticos.",
                new[] { "Movimiento de Suelos", "Fundaciones y Bases", "Estructura Metálica", "Cerramientos y Cubierta", "Pisos Industriales" }
            )
        };

        foreach (var (nombre, descripcion, etapas) in demoPlantillas)
        {
            // Idempotente: antes de crear, comprobar por nombre que la plantilla no exista
            var existePlantilla = await context.PlantillasObra.AnyAsync(p => p.Nombre == nombre);
            if (!existePlantilla)
            {
                var plantilla = new PlantillaObra
                {
                    Id = Guid.NewGuid(),
                    Nombre = nombre,
                    Descripcion = descripcion
                };

                int orden = 1;
                foreach (var nombreEtapa in etapas)
                {
                    plantilla.Etapas.Add(new EtapaPlantilla
                    {
                        Id = Guid.NewGuid(),
                        NombreEtapa = nombreEtapa,
                        Orden = orden++
                    });
                }

                context.PlantillasObra.Add(plantilla);
                logger.LogInformation("Plantilla de prueba creada: {Nombre} con {EtapasCount} etapas",
                    nombre, etapas.Length);
            }
        }

        await context.SaveChangesAsync();
    }
}
