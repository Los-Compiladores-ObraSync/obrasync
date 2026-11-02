using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Api.Extensions;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class DatabaseSeedingTests
{
    private static ServiceProvider CreateTestServiceProvider(string dbName)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.AddDebug());

        services.AddDbContext<AuthDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<AppUser, AppRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequiredLength = 6;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AuthDbContext>()
        .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SeedDatabaseAsync_PrimeraEjecucion_DebeCrearRolesUsuariosYObrasConEtapasAsignadasAlCapataz()
    {
        // Arrange
        var sp = CreateTestServiceProvider($"DB_Seed_Test_{Guid.NewGuid()}");
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();

        // Act
        await DatabaseMigrationExtensions.SeedDatabaseAsync(scope.ServiceProvider);

        // Assert: 1. Roles
        Assert.True(await roleManager.RoleExistsAsync(UserRoles.Admin));
        Assert.True(await roleManager.RoleExistsAsync(UserRoles.Capataz));
        Assert.True(await roleManager.RoleExistsAsync(UserRoles.Comprador));
        Assert.True(await roleManager.RoleExistsAsync(UserRoles.Proveedor));

        // Assert: 2. Usuarios
        var capataz = await userManager.FindByEmailAsync("capataz@obrasync.com");
        Assert.NotNull(capataz);
        Assert.Equal("Carlos Capataz", capataz.NombreCompleto);
        Assert.True(await userManager.IsInRoleAsync(capataz, UserRoles.Capataz));

        // Assert: 3. Obras de ejemplo
        var obras = await context.Obras
            .Include(o => o.Etapas)
            .OrderBy(o => o.Nombre)
            .ToListAsync();

        Assert.Equal(3, obras.Count);

        // Obra 1: Complejo Industrial Sur
        var obraIndustrial = obras.FirstOrDefault(o => o.Nombre == "Complejo Industrial Sur");
        Assert.NotNull(obraIndustrial);
        Assert.Equal("Ruta 2 Km 45, Berazategui", obraIndustrial.Ubicacion);
        Assert.Equal(capataz.Id, obraIndustrial.CapatazId);
        Assert.Equal(4, obraIndustrial.Etapas.Count);
        Assert.All(obraIndustrial.Etapas, e => Assert.Equal("Pendiente", e.Estado));
        Assert.Contains(obraIndustrial.Etapas, e => e.NombreEtapa == "Movimiento de Suelos");
        Assert.Contains(obraIndustrial.Etapas, e => e.NombreEtapa == "Fundaciones");
        Assert.Contains(obraIndustrial.Etapas, e => e.NombreEtapa == "Estructura Metálica");
        Assert.Contains(obraIndustrial.Etapas, e => e.NombreEtapa == "Cerramientos");

        // Obra 2: Residencial Parque
        var obraResidencial = obras.FirstOrDefault(o => o.Nombre == "Residencial Parque");
        Assert.NotNull(obraResidencial);
        Assert.Equal("Av. del Libertador 4500, CABA", obraResidencial.Ubicacion);
        Assert.Equal(capataz.Id, obraResidencial.CapatazId);
        Assert.Equal(3, obraResidencial.Etapas.Count);
        Assert.All(obraResidencial.Etapas, e => Assert.Equal("Pendiente", e.Estado));
        Assert.Contains(obraResidencial.Etapas, e => e.NombreEtapa == "Cimientos");
        Assert.Contains(obraResidencial.Etapas, e => e.NombreEtapa == "Estructura");
        Assert.Contains(obraResidencial.Etapas, e => e.NombreEtapa == "Instalaciones");

        // Obra 3: Torre Alvear
        var obraAlvear = obras.FirstOrDefault(o => o.Nombre == "Torre Alvear");
        Assert.NotNull(obraAlvear);
        Assert.Equal("Av. Alvear 1850, CABA", obraAlvear.Ubicacion);
        Assert.Equal(capataz.Id, obraAlvear.CapatazId);
        Assert.Equal(4, obraAlvear.Etapas.Count);
        Assert.All(obraAlvear.Etapas, e => Assert.Equal("Pendiente", e.Estado));
        Assert.Contains(obraAlvear.Etapas, e => e.NombreEtapa == "Cimientos");
        Assert.Contains(obraAlvear.Etapas, e => e.NombreEtapa == "Estructura");
        Assert.Contains(obraAlvear.Etapas, e => e.NombreEtapa == "Mampostería");
        Assert.Contains(obraAlvear.Etapas, e => e.NombreEtapa == "Terminaciones");
    }

    [Fact]
    public async Task SeedDatabaseAsync_EjecutadoDosVeces_DebeSerIdempotenteYNoDuplicarObrasNiEtapasNiUsuarios()
    {
        // Arrange
        var sp = CreateTestServiceProvider($"DB_Seed_Idempotence_{Guid.NewGuid()}");
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        // Act 1: Primer sembrado
        await DatabaseMigrationExtensions.SeedDatabaseAsync(scope.ServiceProvider);

        var usuariosPrimerPaso = await context.Users.CountAsync();
        var obrasPrimerPaso = await context.Obras.CountAsync();
        var etapasPrimerPaso = await context.EtapasObras.CountAsync();

        Assert.Equal(4, usuariosPrimerPaso);
        Assert.Equal(3, obrasPrimerPaso);
        Assert.Equal(11, etapasPrimerPaso); // 4 + 3 + 4

        // Act 2: Segundo sembrado (simula reinicio de contenedor Docker)
        await DatabaseMigrationExtensions.SeedDatabaseAsync(scope.ServiceProvider);

        var usuariosSegundoPaso = await context.Users.CountAsync();
        var obrasSegundoPaso = await context.Obras.CountAsync();
        var etapasSegundoPaso = await context.EtapasObras.CountAsync();

        // Assert: Nada debe duplicarse
        Assert.Equal(4, usuariosSegundoPaso);
        Assert.Equal(3, obrasSegundoPaso);
        Assert.Equal(11, etapasSegundoPaso);
    }

    [Fact]
    public async Task ListarObras_ConObrasSembradas_DebeRetornarListadoCompletoConCapatazYEtapas()
    {
        // Arrange
        var sp = CreateTestServiceProvider($"DB_Seed_Controller_{Guid.NewGuid()}");
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await DatabaseMigrationExtensions.SeedDatabaseAsync(scope.ServiceProvider);

        var controller = new ObrasController(context);

        // Act
        var result = await controller.ListarObras();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);

        var json = JsonSerializer.Serialize(okResult.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var obras = doc.RootElement.EnumerateArray().ToList();

        Assert.Equal(3, obras.Count);

        foreach (var obra in obras)
        {
            Assert.Equal("Carlos Capataz", obra.GetProperty("capataz").GetString());
            var capatazId = obra.GetProperty("capatazId").GetGuid();
            Assert.NotEqual(Guid.Empty, capatazId);
            var etapas = obra.GetProperty("etapas").EnumerateArray().ToList();
            Assert.True(etapas.Count >= 3);
            Assert.All(etapas, e => Assert.Equal("Pendiente", e.GetProperty("estado").GetString()));
        }
    }
}
