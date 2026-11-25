using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Obtiene el historial completo de recepciones (parciales, completas y entregas con problema
/// con su motivo y metadatos de fotos), junto con los datos de despacho y saldos de una orden (#217).
/// </summary>
public class ObtenerRecepcionesPorOrdenQueryHandler : IQueryHandler<ObtenerRecepcionesPorOrdenQuery, RecepcionesDeOrdenDto?>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerRecepcionesPorOrdenQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<RecepcionesDeOrdenDto?> Handle(
        ObtenerRecepcionesPorOrdenQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerRecepcionesPorOrdenAsync(query.OrdenCompraId, cancellationToken);
    }
}
