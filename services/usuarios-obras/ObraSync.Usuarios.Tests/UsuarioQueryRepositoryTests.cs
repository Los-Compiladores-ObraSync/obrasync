using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using ObraSync.Usuarios.Infrastructure.Repositories;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class UsuarioQueryRepositoryTests
{
    private static AuthDbContext CrearDbContextInMemory()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseInMemoryDatabase($"DB_UsuarioQuery_{Guid.NewGuid()}")
            .Options;

        return new AuthDbContext(options);
    }

    private static async Task<Guid> SembrarUsuarioAsync(
        AuthDbContext db,
        Guid id,
        string nombreCompleto,
        string rolNombre,
        bool activo = true)
    {
        var rolNormalizado = rolNombre.ToUpperInvariant();
        var rol = await db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == rolNormalizado);
        if (rol is null)
        {
            rol = new AppRole(rolNombre)
            {
                Id = Guid.NewGuid(),
                NormalizedName = rolNormalizado
            };
            db.Roles.Add(rol);
        }

        var email = $"{id:N}@obrasync.test";
        var usuario = new AppUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            NombreCompleto = nombreCompleto,
            Activo = activo,
            EmailConfirmed = true
        };

        db.Users.Add(usuario);
        db.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = usuario.Id,
            RoleId = rol.Id
        });

        await db.SaveChangesAsync();
        return usuario.Id;
    }

    [Fact]
    public async Task GetActivosPorRolPaginadosAsync_PaginaOrdenaDesempataYExcluyeInactivosYOtrosRoles()
    {
        await using var db = CrearDbContextInMemory();
        var repo = new UsuarioQueryRepository(db, new UpperInvariantLookupNormalizer());

        // Bloques idénticos por byte de inicio y fin para que el desempate por Guid coincida en InMemory y SQL Server.
        var idMenor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idMayor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var idZeta = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await SembrarUsuarioAsync(db, idMayor, "Corralón Empate", UserRoles.Proveedor, activo: true);
        await SembrarUsuarioAsync(db, Guid.Parse("22222222-2222-2222-2222-222222222222"), "Aaa Inactivo", UserRoles.Proveedor, activo: false);
        await SembrarUsuarioAsync(db, idZeta, "Zulema Materiales", UserRoles.Proveedor, activo: true);
        await SembrarUsuarioAsync(db, idMenor, "Corralón Empate", UserRoles.Proveedor, activo: true);
        await SembrarUsuarioAsync(db, Guid.Parse("33333333-3333-3333-3333-333333333333"), "Carlos Capataz", UserRoles.Capataz, activo: true);
        await SembrarUsuarioAsync(db, Guid.Parse("44444444-4444-4444-4444-444444444444"), "Admin Sistema", UserRoles.Admin, activo: true);

        var (pag1, total1) = await repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(1, 2));
        var (pag2, total2) = await repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(2, 2));
        var (pag3, total3) = await repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(3, 2));

        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(3, total3);

        Assert.Equal(2, pag1.Count);
        Assert.Equal(idMenor, pag1[0].Id);
        Assert.Equal("Corralón Empate", pag1[0].NombreCompleto);
        Assert.Equal(idMayor, pag1[1].Id);
        Assert.Equal("Corralón Empate", pag1[1].NombreCompleto);

        var unico = Assert.Single(pag2);
        Assert.Equal(idZeta, unico.Id);
        Assert.Equal("Zulema Materiales", unico.NombreCompleto);

        Assert.Empty(pag3);

        var (capataces, totalCapataces) = await repo.GetActivosPorRolPaginadosAsync(UserRoles.Capataz, new ParametrosPagina(1, 10));
        Assert.Equal(1, totalCapataces);
        Assert.Equal("Carlos Capataz", Assert.Single(capataces).NombreCompleto);
    }

    [Fact]
    public async Task GetActivosPorRolPaginadosAsync_NormalizaRolCaseInsensitiveYSinLookupNormalizer()
    {
        await using var db = CrearDbContextInMemory();
        var repoSinNormalizer = new UsuarioQueryRepository(db);

        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        await SembrarUsuarioAsync(db, id, "Pedro Proveedor", UserRoles.Proveedor, activo: true);

        var (itemsMinuscula, totalMinuscula) = await repoSinNormalizer.GetActivosPorRolPaginadosAsync(
            "  proveedor ",
            new ParametrosPagina(1, 10));

        Assert.Equal(1, totalMinuscula);
        Assert.Equal(id, Assert.Single(itemsMinuscula).Id);
    }

    [Fact]
    public async Task GetActivosPorRolPaginadosAsync_SinActivos_RetornaTotalCeroYVacio()
    {
        await using var db = CrearDbContextInMemory();
        var repo = new UsuarioQueryRepository(db, new UpperInvariantLookupNormalizer());

        await SembrarUsuarioAsync(db, Guid.NewGuid(), "Inactivo", UserRoles.Proveedor, activo: false);

        var (items, total) = await repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(1, 10));

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetActivosPorRolPaginadosAsync_ArgumentosInvalidos_LanzaExcepcion()
    {
        await using var db = CrearDbContextInMemory();
        var repo = new UsuarioQueryRepository(db);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            repo.GetActivosPorRolPaginadosAsync("   ", new ParametrosPagina()));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(0, 10)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repo.GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, new ParametrosPagina(1, 51)));
    }

    [Fact]
    public void ConstruirConsultas_TraducenASqlServerConJoinOrdenOffsetYProyeccionMinima()
    {
        var sqlOptions = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=DB_Auth_QueryPlan;Trusted_Connection=True;")
            .Options;

        using var db = new AuthDbContext(sqlOptions);
        var repo = new UsuarioQueryRepository(db, new UpperInvariantLookupNormalizer());

        var sqlConteo = repo.ConstruirConsultaConteo(UserRoles.Proveedor).ToQueryString();
        var sqlPaginado = repo.ConstruirConsultaPaginada(UserRoles.Proveedor, new ParametrosPagina(2, 10)).ToQueryString();

        Assert.Contains("FROM [Usuarios]", sqlConteo, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [AspNetUserRoles]", sqlConteo, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [AspNetRoles]", sqlConteo, StringComparison.Ordinal);
        Assert.Contains("[Activo]", sqlConteo, StringComparison.Ordinal);
        Assert.Contains("[NormalizedName]", sqlConteo, StringComparison.Ordinal);

        Assert.Contains("FROM [Usuarios]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [AspNetUserRoles]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN [AspNetRoles]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("[Activo]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("[NormalizedName]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("ORDER BY [u].[NombreCompleto], [u].[Id]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("OFFSET", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("FETCH NEXT", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("[u].[Id]", sqlPaginado, StringComparison.Ordinal);
        Assert.Contains("[u].[NombreCompleto]", sqlPaginado, StringComparison.Ordinal);
        Assert.DoesNotContain("PasswordHash", sqlPaginado, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityStamp", sqlPaginado, StringComparison.Ordinal);
    }
}
