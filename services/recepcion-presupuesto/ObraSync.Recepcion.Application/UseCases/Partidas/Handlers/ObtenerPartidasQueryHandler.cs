using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Domain.Presupuestos;

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

        var partidas = await dbQuery
            .OrderBy(p => p.CodigoPartida)
            .ToListAsync(cancellationToken);

        // Filtrado por etapa normalizada (sin distinguir mayúsculas, espacios ni tildes) (#196)
        if (!string.IsNullOrWhiteSpace(query.Etapa))
        {
            partidas = partidas
                .Where(p => NormalizadorEtapa.SonEquivalentes(p.Etapa, query.Etapa))
                .ToList();
        }

        return partidas.Select(p => p.ToDto()).ToList();
    }
}
