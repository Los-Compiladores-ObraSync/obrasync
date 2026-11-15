using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class ObtenerPartidasQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerPartidasQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<PartidaPresupuestariaDto>> Handle(
        ObtenerPartidasQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.PartidasPresupuestarias.AsNoTracking();

        if (query.ObraId.HasValue && query.ObraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(p => p.ObraId == query.ObraId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Etapa))
        {
            var etapaFiltro = query.Etapa.Trim();
            dbQuery = dbQuery.Where(p => p.Etapa != null && p.Etapa.ToLower() == etapaFiltro.ToLower());
        }

        var partidas = await dbQuery
            .OrderBy(p => p.CodigoPartida)
            .ToListAsync(cancellationToken);

        return partidas.Select(p => new PartidaPresupuestariaDto
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
        }).ToList();
    }
}
