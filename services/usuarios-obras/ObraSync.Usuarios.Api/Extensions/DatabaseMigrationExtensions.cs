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

        try
        {
            var context = services.GetRequiredService<AuthDbContext>();

            // 1. Aplica migraciones pendientes si la base de datos SQL Server recién arranca en Docker
            if (context.Database.IsSqlServer())
            {
                await context.Database.MigrateAsync();
            }

            var roleManager = services.GetRequiredService<RoleManager<AppRole>>();

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
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ocurrió un error al aplicar migraciones o sembrar la base de datos.");
        }
    }
}