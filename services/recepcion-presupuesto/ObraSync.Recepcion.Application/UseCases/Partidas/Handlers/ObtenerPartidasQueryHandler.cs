using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Application.UseCases.Partidas.Handlers;

public class ObtenerPartidasQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerPartidasQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<PartidaPresupuestariaDto>> Handle(
        ObtenerPartidasQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.PartidasPresupuestarias.AsNoTracking();

        if (query.ObraId.HasValue && query.ObraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(p => p.ObraId == query.ObraId.Value);
        }

        var p = query.Paginacion ?? new ParametrosPagina();
        int total;
        List<PartidaPresupuestaria> partidas;

        // Filtrado por etapa normalizada (sin distinguir mayúsculas, espacios ni tildes) (#196)
        if (!string.IsNullOrWhiteSpace(query.Etapa))
        {
            var todas = await dbQuery.ToListAsync(cancellationToken);
            var filtradas = todas
                .Where(x => NormalizadorEtapa.SonEquivalentes(x.Etapa, query.Etapa))
                .OrderBy(x => x.CodigoPartida)
                .ThenBy(x => x.Id)
                .ToList();

            total = filtradas.Count;
            partidas = filtradas.Skip(p.Saltear).Take(p.Tamano).ToList();
        }
        else
        {
            total = await dbQuery.CountAsync(cancellationToken);
            partidas = await dbQuery
                .OrderBy(x => x.CodigoPartida)
                .ThenBy(x => x.Id)
                .Skip(p.Saltear)
                .Take(p.Tamano)
                .ToListAsync(cancellationToken);
        }

        return new PaginaDe<PartidaPresupuestariaDto>(partidas.Select(p => p.ToDto()).ToList(), total);
    }
}
