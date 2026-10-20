using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Requisiciones.Infrastructure.Persistence;

namespace ObraSync.Requisiciones.Api.Extensions
{
    public static class DatabaseMigrationExtensions
    {
        public static async Task MigrateDatabaseAsync(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<RequisicionesDbContext>();

            if (context.Database.IsSqlServer())
            {
                await context.Database.MigrateAsync();
            }
        }
    }
}
