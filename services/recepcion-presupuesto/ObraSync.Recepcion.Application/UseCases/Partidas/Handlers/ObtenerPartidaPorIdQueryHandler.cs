using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class ObtenerPartidaPorIdQueryHandler : IQueryHandler<ObtenerPartidaPorIdQuery, PartidaPresupuestariaDto?>
{
    private readonly IPartidaQueries _queries;

    public ObtenerPartidaPorIdQueryHandler(IPartidaQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<PartidaPresupuestariaDto?> Handle(
        ObtenerPartidaPorIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerPartidaPorIdAsync(query.Id, cancellationToken);
    }
}
