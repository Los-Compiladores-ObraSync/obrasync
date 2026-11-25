using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.Interfaces;

public interface IRecepcionRepository
{
    Task<(EstadoRecepcion Estado, DateTime CreatedAt)?> ObtenerUltimaRecepcionResumenAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    Task<bool> ExisteRecepcionCompletaAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    Task<bool> ExisteNumeroRemitoAsync(Guid ordenCompraId, string numeroRemito, CancellationToken cancellationToken = default);
    Task<Dictionary<Guid, decimal>> ObtenerCantidadesRecibidasPorArticuloAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    void AgregarRecepcion(Domain.Entities.Recepcion recepcion);
}
