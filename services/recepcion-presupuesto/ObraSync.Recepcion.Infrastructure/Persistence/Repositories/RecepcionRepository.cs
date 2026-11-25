using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Repositories;

public class RecepcionRepository : IRecepcionRepository
{
    private readonly RecepcionDbContext _context;

    public RecepcionRepository(RecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<(EstadoRecepcion Estado, DateTime CreatedAt)?> ObtenerUltimaRecepcionResumenAsync(
        Guid ordenCompraId,
        CancellationToken cancellationToken = default)
    {
        var ultima = await _context.Recepciones
            .AsNoTracking()
            .Where(r => r.OrdenCompraId == ordenCompraId)
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.FechaRecepcion)
            .ThenByDescending(r => r.Id)
            .Select(r => new { r.Estado, r.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        return ultima is null ? null : (ultima.Estado, ultima.CreatedAt);
    }

    public Task<bool> ExisteRecepcionCompletaAsync(Guid ordenCompraId, CancellationToken cancellationToken = default)
    {
        return _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == ordenCompraId &&
                     r.Estado == EstadoRecepcion.Completa,
                cancellationToken);
    }

    public Task<bool> ExisteNumeroRemitoAsync(Guid ordenCompraId, string numeroRemito, CancellationToken cancellationToken = default)
    {
        return _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == ordenCompraId &&
                     r.NumeroRemito == numeroRemito,
                cancellationToken);
    }

    public Task<Dictionary<Guid, decimal>> ObtenerCantidadesRecibidasPorArticuloAsync(
        Guid ordenCompraId,
        CancellationToken cancellationToken = default)
    {
        return _context.Recepciones
            .AsNoTracking()
            .Where(r => r.OrdenCompraId == ordenCompraId && r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles.Select(d => new { d.ArticuloId, d.CantidadRecibida }))
            .GroupBy(d => d.ArticuloId)
            .Select(g => new { ArticuloId = g.Key, Total = g.Sum(d => d.CantidadRecibida) })
            .ToDictionaryAsync(x => x.ArticuloId, x => x.Total, cancellationToken);
    }

    public void AgregarRecepcion(Domain.Entities.Recepcion recepcion)
    {
        _context.Recepciones.Add(recepcion);
    }
}
