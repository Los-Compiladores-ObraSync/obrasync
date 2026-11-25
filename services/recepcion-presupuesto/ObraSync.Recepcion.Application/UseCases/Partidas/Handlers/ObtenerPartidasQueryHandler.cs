using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class ObtenerPartidasQueryHandler : IQueryHandler<ObtenerPartidasQuery, PaginaDe<PartidaPresupuestariaDto>>
{
    private readonly IPartidaQueries _queries;

    public ObtenerPartidasQueryHandler(IPartidaQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<PaginaDe<PartidaPresupuestariaDto>> Handle(
        ObtenerPartidasQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerPartidasAsync(query, cancellationToken);
    }
}
