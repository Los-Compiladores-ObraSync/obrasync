using System;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class ObtenerPartidaPorIdQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerPartidaPorIdQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PartidaPresupuestariaDto?> Handle(
        ObtenerPartidaPorIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var p = await _context.PartidasPresupuestarias
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken);

        return p?.ToDto();
    }
}
