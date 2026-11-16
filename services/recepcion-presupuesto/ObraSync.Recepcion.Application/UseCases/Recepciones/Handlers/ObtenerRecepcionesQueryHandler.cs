using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerRecepcionesQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerRecepcionesQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<RecepcionDto>> Handle(
        ObtenerRecepcionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking();

        if (query.ObraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.ObraId == query.ObraId.Value);
        }
        else if (query.ObrasPermitidas != null)
        {
            dbQuery = dbQuery.Where(r => query.ObrasPermitidas.Contains(r.ObraId));
        }

        if (query.OrdenCompraId.HasValue)
        {
            dbQuery = dbQuery.Where(r => r.OrdenCompraId == query.OrdenCompraId.Value);
        }

        var recepciones = await dbQuery.ToListAsync(cancellationToken);
        return recepciones.Select(r => r.ToDto()).ToList();
    }
}
