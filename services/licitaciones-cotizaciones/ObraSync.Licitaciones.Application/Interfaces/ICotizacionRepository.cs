using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ICotizacionRepository
{
    Task<bool> ExistsByLicitacionAndProveedorAsync(
        Guid licitacionId,
        Guid proveedorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De las licitaciones indicadas, devuelve los Id de las que el proveedor ya cotizó.
    /// Una sola consulta para toda la lista (evita consultar de a una licitación).
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetLicitacionIdsCotizadasPorProveedorAsync(
        Guid proveedorId,
        IReadOnlyCollection<Guid> licitacionIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Cotizacion>> GetByLicitacionIdAsync(
        Guid licitacionId,
        CancellationToken cancellationToken = default);

    Task<Cotizacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Cotizacion cotizacion,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}