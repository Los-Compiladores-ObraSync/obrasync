using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class CotizacionRepository : ICotizacionRepository
{
    private readonly LicitacionesDbContext _context;

    public CotizacionRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> ExistsByLicitacionAndProveedorAsync(
        Guid licitacionId,
        Guid proveedorId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Cotizaciones
            .AnyAsync(c => c.LicitacionId == licitacionId && c.ProveedorId == proveedorId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetLicitacionIdsCotizadasPorProveedorAsync(
        Guid proveedorId,
        IReadOnlyCollection<Guid> licitacionIds,
        CancellationToken cancellationToken = default)
    {
        if (licitacionIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var ids = licitacionIds.ToList();
        return await _context.Cotizaciones
            .AsNoTracking()
            .Where(c => c.ProveedorId == proveedorId && ids.Contains(c.LicitacionId))
            .Select(c => c.LicitacionId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Cotizacion>> GetByLicitacionIdAsync(
        Guid licitacionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Cotizaciones
            .AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.LicitacionId == licitacionId)
            .OrderByDescending(c => c.FechaCreacion)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(
        Cotizacion cotizacion,
        CancellationToken cancellationToken = default)
    {
        _context.Cotizaciones.Add(cotizacion);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Cotizacion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
    return await _context.Cotizaciones
        .Include(c => c.Items)
        .FirstOrDefaultAsync(c => c.CotizacionId == id, cancellationToken);
    }
}
