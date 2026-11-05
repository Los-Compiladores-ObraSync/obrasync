using Microsoft.EntityFrameworkCore;
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
}