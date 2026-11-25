using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Recupera el binario de una foto de evidencia junto con ObraId y ProveedorId
/// para validación estricta de permisos antes de responder la descarga (#217).
/// </summary>
public class ObtenerFotoRecepcionQueryHandler : IQueryHandler<ObtenerFotoRecepcionQuery, FotoDescargaDto?>
{
    private readonly IRecepcionQueries _queries;

    public ObtenerFotoRecepcionQueryHandler(IRecepcionQueries queries)
    {
        _queries = queries ?? throw new ArgumentNullException(nameof(queries));
    }

    public Task<FotoDescargaDto?> Handle(
        ObtenerFotoRecepcionQuery query,
        CancellationToken cancellationToken = default)
    {
        return _queries.ObtenerFotoRecepcionAsync(query.RecepcionId, query.FotoId, cancellationToken);
    }
}
