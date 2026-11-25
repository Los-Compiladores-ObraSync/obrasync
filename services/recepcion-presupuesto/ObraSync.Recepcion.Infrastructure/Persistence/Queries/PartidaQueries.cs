using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Infrastructure.Persistence.Queries;

public class PartidaQueries : IPartidaQueries
{
    private readonly RecepcionDbContext _context;

    public PartidaQueries(RecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<PartidaPresupuestariaDto>> ObtenerPartidasAsync(
        ObtenerPartidasQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.PartidasPresupuestarias.AsNoTracking();

        if (query.ObraId.HasValue && query.ObraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(p => p.ObraId == query.ObraId.Value);
        }

        if (query.ObrasPermitidas is not null)
        {
            var permitidas = query.ObrasPermitidas;
            dbQuery = dbQuery.Where(x => permitidas.Contains(x.ObraId));
        }

        var p = query.Paginacion ?? new ParametrosPagina();
        int total;
        List<PartidaPresupuestaria> partidas;

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

        return new PaginaDe<PartidaPresupuestariaDto>(partidas.Select(x => x.ToDto()).ToList(), total);
    }

    public async Task<PartidaPresupuestariaDto?> ObtenerPartidaPorIdAsync(
        Guid partidaId,
        CancellationToken cancellationToken = default)
    {
        var p = await _context.PartidasPresupuestarias
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == partidaId, cancellationToken);

        return p?.ToDto();
    }
}
