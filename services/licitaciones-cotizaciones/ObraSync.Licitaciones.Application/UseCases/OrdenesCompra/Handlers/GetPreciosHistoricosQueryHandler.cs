using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;

public class GetPreciosHistoricosQueryHandler
    : IRequestHandler<GetPreciosHistoricosQuery, IReadOnlyList<PrecioHistoricoDto>>
{
    private readonly IOrdenCompraRepository _ordenCompraRepository;

    public GetPreciosHistoricosQueryHandler(IOrdenCompraRepository ordenCompraRepository)
    {
        _ordenCompraRepository = ordenCompraRepository ?? throw new ArgumentNullException(nameof(ordenCompraRepository));
    }

    public Task<IReadOnlyList<PrecioHistoricoDto>> Handle(
        GetPreciosHistoricosQuery query,
        CancellationToken cancellationToken = default)
    {
        return _ordenCompraRepository.GetUltimosPreciosAsync(query.ArticuloIds, cancellationToken);
    }
}
