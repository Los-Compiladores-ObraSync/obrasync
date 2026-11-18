using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class OrdenCompraRepository : IOrdenCompraRepository
{
    private readonly LicitacionesDbContext _context;

    public OrdenCompraRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(OrdenCompra ordenCompra, CancellationToken cancellationToken = default)
    {
        await _context.Set<OrdenCompra>().AddAsync(ordenCompra, cancellationToken);
    }

    public async Task<OrdenCompra?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var orden = await _context.OrdenesCompra
            .Include(oc => oc.Items)
            .FirstOrDefaultAsync(oc => oc.OrdenCompraId == id, cancellationToken);

        if (orden is not null && orden.ObraId == Guid.Empty)
        {
            orden.ObraId = await _context.Licitaciones
                .AsNoTracking()
                .Where(l => l.LicitacionId == orden.LicitacionId)
                .Select(l => l.ObraId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return orden;
    }

    public Task UpdateAsync(OrdenCompra ordenCompra, CancellationToken cancellationToken = default)
    {
        var entry = _context.Entry(ordenCompra);
        if (entry.State == EntityState.Detached)
        {
            _context.OrdenesCompra.Update(ordenCompra);
        }

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<OrdenCompra>> GetByLicitacionIdsAsync(
        IReadOnlyCollection<Guid> licitacionIds,
        CancellationToken cancellationToken = default)
    {
        if (licitacionIds.Count == 0)
        {
            return Array.Empty<OrdenCompra>();
        }

        var ids = licitacionIds.ToList();
        return await _context.OrdenesCompra
            .AsNoTracking()
            .Where(oc => ids.Contains(oc.LicitacionId))
            .ToListAsync(cancellationToken);
    }

    public async Task<PaginaDe<OrdenCompra>> GetFilteredAsync(
        Guid? obraId = null,
        Guid? licitacionId = null,
        Guid? proveedorId = null,
        string? estado = null,
        ParametrosPagina? paginacion = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.OrdenesCompra
            .AsNoTracking()
            .AsQueryable();

        if (obraId is Guid oid && oid != Guid.Empty)
        {
            var licitacionIdsObra = _context.Licitaciones
                .AsNoTracking()
                .Where(l => l.ObraId == oid)
                .Select(l => l.LicitacionId);

            query = query.Where(oc => licitacionIdsObra.Contains(oc.LicitacionId));
        }

        if (licitacionId is Guid lid && lid != Guid.Empty)
        {
            query = query.Where(oc => oc.LicitacionId == lid);
        }

        if (proveedorId is Guid pid && pid != Guid.Empty)
        {
            query = query.Where(oc => oc.ProveedorId == pid);
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            var estadoNormalizado = estado.Trim().ToLowerInvariant();
            query = query.Where(oc => oc.Estado.ToLower() == estadoNormalizado);
        }

        var total = await query.CountAsync(cancellationToken);

        var pag = paginacion ?? new ParametrosPagina();
        var items = await query
            .OrderByDescending(oc => oc.FechaEmision)
            .ThenBy(oc => oc.OrdenCompraId)
            .Skip(pag.Saltear)
            .Take(pag.Tamano)
            .ToListAsync(cancellationToken);

        if (items.Count > 0)
        {
            var licitacionIds = items.Select(i => i.LicitacionId).Distinct().ToList();
            var obrasPorLicitacion = await _context.Licitaciones
                .AsNoTracking()
                .Where(l => licitacionIds.Contains(l.LicitacionId))
                .Select(l => new { l.LicitacionId, l.ObraId })
                .ToDictionaryAsync(x => x.LicitacionId, x => x.ObraId, cancellationToken);

            foreach (var item in items)
            {
                if (obrasPorLicitacion.TryGetValue(item.LicitacionId, out var obraItemId))
                {
                    item.ObraId = obraItemId;
                }
            }
        }

        return new PaginaDe<OrdenCompra>(items, total);
    }
}