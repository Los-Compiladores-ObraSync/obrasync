using System;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
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

        if (p == null) return null;

        return new PartidaPresupuestariaDto
        {
            Id = p.Id,
            ObraId = p.ObraId,
            Etapa = p.Etapa,
            CodigoPartida = p.CodigoPartida,
            Descripcion = p.Descripcion,
            MontoAsignado = p.MontoAsignado,
            MontoComprometido = p.MontoComprometido,
            MontoEjecutado = p.MontoEjecutado,
            MontoDisponible = p.MontoDisponible,
            Moneda = p.Moneda,
            CreatedAt = p.CreatedAt
        };
    }
}
