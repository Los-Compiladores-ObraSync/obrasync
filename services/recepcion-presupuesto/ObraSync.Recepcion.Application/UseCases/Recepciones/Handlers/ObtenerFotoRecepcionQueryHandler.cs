using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Recupera el binario de una foto de evidencia junto con ObraId y ProveedorId
/// para validación estricta de permisos antes de responder la descarga (#217).
/// </summary>
public class ObtenerFotoRecepcionQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerFotoRecepcionQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<FotoDescargaDto?> Handle(
        ObtenerFotoRecepcionQuery query,
        CancellationToken cancellationToken = default)
    {
        var foto = await _context.FotosRecepcion
            .AsNoTracking()
            .Where(f => f.Id == query.FotoId && f.RecepcionId == query.RecepcionId)
            .Select(f => new
            {
                f.Id,
                f.RecepcionId,
                f.NombreArchivo,
                f.ContentType,
                f.Contenido,
                f.Recepcion.ObraId,
                f.Recepcion.OrdenCompraId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (foto is null)
        {
            return null;
        }

        var proveedorId = await _context.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == foto.OrdenCompraId)
            .Select(o => (Guid?)o.ProveedorId)
            .FirstOrDefaultAsync(cancellationToken);

        return new FotoDescargaDto(
            foto.Id,
            foto.RecepcionId,
            foto.ObraId,
            foto.OrdenCompraId,
            proveedorId,
            foto.NombreArchivo,
            foto.ContentType,
            foto.Contenido);
    }
}
