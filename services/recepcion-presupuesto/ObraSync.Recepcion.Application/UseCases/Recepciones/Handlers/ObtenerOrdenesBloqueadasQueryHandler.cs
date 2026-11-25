using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerOrdenesBloqueadasQueryHandler : IQueryHandler<ObtenerOrdenesBloqueadasQuery, PaginaDe<OrdenBloqueadaDto>>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerOrdenesBloqueadasQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<PaginaDe<OrdenBloqueadaDto>> Handle(
        ObtenerOrdenesBloqueadasQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _queries.ObtenerOrdenesBloqueadasAsync(query, cancellationToken);
    }
}
