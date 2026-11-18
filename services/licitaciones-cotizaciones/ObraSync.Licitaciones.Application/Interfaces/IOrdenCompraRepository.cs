using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface IOrdenCompraRepository
{
    Task AddAsync(OrdenCompra ordenCompra, CancellationToken cancellationToken = default);

    Task<OrdenCompra?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpdateAsync(OrdenCompra ordenCompra, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrdenCompra>> GetByLicitacionIdsAsync(
        IReadOnlyCollection<Guid> licitacionIds,
        CancellationToken cancellationToken = default);

    Task<PaginaDe<OrdenCompra>> GetFilteredAsync(
        Guid? obraId = null,
        Guid? licitacionId = null,
        Guid? proveedorId = null,
        string? estado = null,
        ParametrosPagina? paginacion = null,
        CancellationToken cancellationToken = default);
}