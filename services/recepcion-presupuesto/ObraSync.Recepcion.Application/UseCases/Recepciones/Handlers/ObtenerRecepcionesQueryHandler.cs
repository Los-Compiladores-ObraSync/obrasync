using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public async Task<PaginaDe<RecepcionDto>> Handle(
        ObtenerRecepcionesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.Recepciones.AsNoTracking();

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

        // Total con los mismos filtros sin Include ni Skip/Take (#185)
        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var recepciones = await dbQuery
            .Include(r => r.Detalles)
            .OrderByDescending(r => r.FechaRecepcion)
            .ThenByDescending(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        return new PaginaDe<RecepcionDto>(recepciones.Select(r => r.ToDto()).ToList(), total);
    }
}
