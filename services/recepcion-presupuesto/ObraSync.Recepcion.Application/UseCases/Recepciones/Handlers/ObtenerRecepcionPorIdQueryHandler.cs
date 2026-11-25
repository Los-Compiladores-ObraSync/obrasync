using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerRecepcionPorIdQueryHandler : IQueryHandler<ObtenerRecepcionPorIdQuery, RecepcionDto?>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerRecepcionPorIdQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<RecepcionDto?> Handle(
        ObtenerRecepcionPorIdQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerRecepcionPorIdAsync(query.Id, cancellationToken);
    }
}
