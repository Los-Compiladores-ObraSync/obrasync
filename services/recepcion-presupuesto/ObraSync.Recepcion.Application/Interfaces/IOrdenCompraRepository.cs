using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.Interfaces;

public interface IOrdenCompraRepository
{
    Task<OrdenCompra?> ObtenerPorIdAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    Task<bool> ExisteAsync(Guid ordenCompraId, CancellationToken cancellationToken = default);
    void Agregar(OrdenCompra ordenCompra);
}
