using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class LicitacionRepository : ILicitacionRepository
{
    private readonly LicitacionesDbContext _context;

    public LicitacionRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<RequisicionAprobada?> ObtenerRequisicionAsync(
        Guid requisicionId,
        CancellationToken ct = default) =>
        _context.RequisicionesAprobadas
            .AsNoTracking()
            .Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.RequisicionId == requisicionId, ct);

    public Task<bool> ExisteAbiertaAsync(
        Guid requisicionId,
        CancellationToken ct = default) =>
        _context.Licitaciones
            .AnyAsync(l => l.RequisicionId == requisicionId && l.Estado == EstadoLicitacion.Abierta, ct);

    public async Task AgregarAsync(
        Licitacion licitacion,
        CancellationToken ct = default)
    {
        _context.Licitaciones.Add(licitacion);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<Licitacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.Licitaciones
            .AsNoTracking()
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.LicitacionId == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Licitacion>> GetFilteredAsync(
        Guid? obraId = null,
        string? estado = null,
        string? rolUsuario = null,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Licitaciones
            .AsNoTracking()
            .Include(l => l.Items)
            .AsQueryable();

        if (obraId.HasValue && obraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(l => l.ObraId == obraId.Value);
        }

        // Regla de negocio: Si el rol es Proveedor, solo puede ver licitaciones en estado Abierta
        if (string.Equals(rolUsuario, "Proveedor", StringComparison.OrdinalIgnoreCase))
        {
            dbQuery = dbQuery.Where(l => l.Estado == EstadoLicitacion.Abierta);
        }
        else if (!string.IsNullOrWhiteSpace(estado))
        {
            var estadoNormalizado = estado.Trim();
            if (!Enum.TryParse<EstadoLicitacion>(estadoNormalizado, ignoreCase: true, out var estadoEnum))
            {
                return Array.Empty<Licitacion>();
            }

            dbQuery = dbQuery.Where(l => l.Estado == estadoEnum);
        }

        return await dbQuery
            .OrderByDescending(l => l.FechaCreacion)
            .ToListAsync(cancellationToken);
    }
}
