using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Api.Extensions;

public static class DatabaseMigrationExtensions
{
    public static async Task SeedDatabaseAsync(this IApplicationBuilder app)
    {
        using var scope = app.ApplicationServices.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<AuthDbContext>>();

        // Se elimina el try-catch silencioso: si la conexión o migración falla,
        // la excepción escala para que el contenedor falle (fail-fast) en Docker.
        var context = services.GetRequiredService<AuthDbContext>();

        // 1. Aplica migraciones pendientes si la base de datos SQL Server recién arranca en Docker
        if (context.Database.IsSqlServer())
        {
            await context.Database.MigrateAsync();
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
    }
}