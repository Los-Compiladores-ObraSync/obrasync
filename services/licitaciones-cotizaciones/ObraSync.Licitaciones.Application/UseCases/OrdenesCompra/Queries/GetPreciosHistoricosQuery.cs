using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

public record GetPreciosHistoricosQuery(IReadOnlyCollection<Guid> ArticuloIds)
    : IRequest<IReadOnlyList<PrecioHistoricoDto>>;
