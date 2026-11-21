using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerRecepcionPorIdQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerRecepcionPorIdQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<RecepcionDto?> Handle(
        ObtenerRecepcionPorIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var recepcion = await _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken);

        if (recepcion is null)
        {
            return null;
        }

        // Proyección liviana sin traer varbinary(max) (#217)
        var fotos = await _context.FotosRecepcion
            .AsNoTracking()
            .Where(f => f.RecepcionId == recepcion.Id)
            .OrderBy(f => f.Orden)
            .ThenBy(f => f.CreatedAt)
            .Select(f => RecepcionMappings.CrearFotoDto(
                f.Id,
                f.RecepcionId,
                f.NombreArchivo,
                f.ContentType,
                f.TamanoBytes,
                f.Orden))
            .ToListAsync(cancellationToken);

        return recepcion.ToDto(fotos);
    }
}
