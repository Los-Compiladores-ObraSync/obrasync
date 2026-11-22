using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Services;

public class DestinatariosNotificacionService : IDestinatariosNotificacionService
{
    private static readonly string[] TodosLosRoles =
    [
        UserRoles.Capataz,
        UserRoles.Comprador,
        UserRoles.Proveedor,
        UserRoles.Admin
    ];

    private readonly AuthDbContext _db;

    public DestinatariosNotificacionService(AuthDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<Guid>> GetUsuarioIdsPorRolesAsync(
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default)
    {
        if (roles == null || roles.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var rolesNormalizados = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => TodosLosRoles.FirstOrDefault(
                conocido => string.Equals(conocido, r.Trim(), StringComparison.OrdinalIgnoreCase)) ?? r.Trim())
            .Distinct()
            .ToList();

        if (rolesNormalizados.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await (
            from u in _db.Users.AsNoTracking()
            join ur in _db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where u.Activo && r.Name != null && rolesNormalizados.Contains(r.Name)
            select u.Id
        ).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<Guid?> GetCapatazIdDeObraAsync(
        Guid obraId,
        CancellationToken cancellationToken = default)
    {
        if (obraId == Guid.Empty)
        {
            return null;
        }

        return await _db.Obras
            .AsNoTracking()
            .Where(o => o.Id == obraId && o.Activa && o.Capataz != null && o.Capataz.Activo)
            .Select(o => (Guid?)o.CapatazId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> EsUsuarioActivoAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        if (usuarioId == Guid.Empty)
        {
            return false;
        }

        return await _db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == usuarioId && u.Activo, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FiltrarUsuariosActivosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken = default)
    {
        if (usuarioIds == null || usuarioIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = usuarioIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        return await _db.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Activo)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);
    }
}
