using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ICalificacionProveedorRepository
{
    Task<CalificacionProveedor?> GetByProveedorIdAsync(
        Guid proveedorId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, int>> GetCalificacionesByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default);

    Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default);
}
