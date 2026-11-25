using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Repositories;

/// <summary>
/// Consultas EF Core paginadas directamente en base de datos sobre Usuarios, UserRoles y Roles (#254).
/// </summary>
public class UsuarioQueryRepository : IUsuarioQueryRepository
{
    private readonly AuthDbContext _db;
    private readonly ILookupNormalizer? _lookupNormalizer;

    public UsuarioQueryRepository(AuthDbContext db, ILookupNormalizer? lookupNormalizer = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _lookupNormalizer = lookupNormalizer;
    }

    public async Task<(IReadOnlyList<UsuarioResumenDto> Items, int Total)> GetActivosPorRolPaginadosAsync(
        string rol,
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default)
    {
        ValidarArgumentos(rol, paginacion);

        var queryFiltrada = ConstruirConsultaFiltradaPorRol(rol);

        var total = await queryFiltrada.CountAsync(cancellationToken);
        if (total == 0 || paginacion.Saltear >= total)
        {
            return (Array.Empty<UsuarioResumenDto>(), total);
        }

        var items = await ConstruirConsultaPaginada(queryFiltrada, paginacion)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    internal IQueryable<AppUser> ConstruirConsultaConteo(string rol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rol);
        return ConstruirConsultaFiltradaPorRol(rol);
    }

    internal IQueryable<UsuarioResumenDto> ConstruirConsultaPaginada(
        string rol,
        ParametrosPagina paginacion)
    {
        ValidarArgumentos(rol, paginacion);
        return ConstruirConsultaPaginada(ConstruirConsultaFiltradaPorRol(rol), paginacion);
    }

    private IQueryable<AppUser> ConstruirConsultaFiltradaPorRol(string rol)
    {
        var rolTrim = rol.Trim();
        var rolNormalizado = _lookupNormalizer?.NormalizeName(rolTrim) ?? rolTrim.ToUpperInvariant();

        return
            from u in _db.Users.AsNoTracking()
            join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where u.Activo && r.NormalizedName == rolNormalizado
            select u;
    }

    private static IQueryable<UsuarioResumenDto> ConstruirConsultaPaginada(
        IQueryable<AppUser> queryFiltrada,
        ParametrosPagina paginacion)
    {
        return queryFiltrada
            .OrderBy(u => u.NombreCompleto)
            .ThenBy(u => u.Id)
            .Skip(paginacion.Saltear)
            .Take(paginacion.Tamano)
            .Select(u => new UsuarioResumenDto(u.Id, u.NombreCompleto, u.PhoneNumber));
    }

    private static void ValidarArgumentos(string rol, ParametrosPagina paginacion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rol);
        ArgumentNullException.ThrowIfNull(paginacion);

        var error = paginacion.Validar();
        if (error is not null)
        {
            throw new ArgumentOutOfRangeException(nameof(paginacion), error);
        }
    }
}
