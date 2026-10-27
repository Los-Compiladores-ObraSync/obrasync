using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Repositories;

public class OrdenCompraRepository : IOrdenCompraRepository
{
    private readonly RecepcionDbContext _context;

    public OrdenCompraRepository(RecepcionDbContext context)
    {
        _context = context;
    }

    public async Task<OrdenCompra?> ObtenerPorIdAsync(Guid ordenCompraId, CancellationToken cancellationToken = default)
    {
        return await _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == ordenCompraId, cancellationToken);
    }

    public async Task<bool> ExisteAsync(Guid ordenCompraId, CancellationToken cancellationToken = default)
    {
        return await _context.OrdenesCompra
            .AnyAsync(o => o.Id == ordenCompraId, cancellationToken);
    }

    public void Agregar(OrdenCompra ordenCompra)
    {
        // Solo marca la entidad en el contexto, NO llama a SaveChanges
        _context.OrdenesCompra.Add(ordenCompra);
    }
}
