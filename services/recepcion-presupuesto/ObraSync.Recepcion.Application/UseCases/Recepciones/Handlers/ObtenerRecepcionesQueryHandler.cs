using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerRecepcionesQueryHandler : IQueryHandler<ObtenerRecepcionesQuery, PaginaDe<RecepcionDto>>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerRecepcionesQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<PaginaDe<RecepcionDto>> Handle(
        ObtenerRecepcionesQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerRecepcionesAsync(query, cancellationToken);
    }
}
