using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ICotizacionRepository
{
    Task<bool> ExistsByLicitacionAndProveedorAsync(
        Guid licitacionId,
        Guid proveedorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Cotizacion>> GetByLicitacionIdAsync(
        Guid licitacionId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Cotizacion cotizacion,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
