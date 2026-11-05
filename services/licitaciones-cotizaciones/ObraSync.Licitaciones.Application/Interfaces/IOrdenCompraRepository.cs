using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface IOrdenCompraRepository
{
    Task AddAsync(OrdenCompra ordenCompra, CancellationToken cancellationToken = default);
}