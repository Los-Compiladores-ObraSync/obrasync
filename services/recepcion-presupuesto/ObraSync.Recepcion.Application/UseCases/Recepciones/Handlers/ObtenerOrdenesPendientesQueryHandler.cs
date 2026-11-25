using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerOrdenesPendientesQueryHandler : IQueryHandler<ObtenerOrdenesPendientesQuery, PaginaDe<OrdenPendienteDto>>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerOrdenesPendientesQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<PaginaDe<OrdenPendienteDto>> Handle(
        ObtenerOrdenesPendientesQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerOrdenesPendientesAsync(query, cancellationToken);
    }
}
