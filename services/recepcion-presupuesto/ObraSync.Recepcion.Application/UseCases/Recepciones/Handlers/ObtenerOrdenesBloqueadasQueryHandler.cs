using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Mappings;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Presupuestos;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerOrdenesBloqueadasQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerOrdenesBloqueadasQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<OrdenBloqueadaDto>> Handle(
        ObtenerOrdenesBloqueadasQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var dbQuery = _context.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Estado == EstadoOrdenCompra.Bloqueada);

        if (query.ObraId.HasValue && query.ObraId.Value != Guid.Empty)
        {
            dbQuery = dbQuery.Where(o => o.ObraId == query.ObraId.Value);
        }

        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var ordenes = await dbQuery
            .OrderByDescending(o => o.FechaBloqueo ?? o.UpdatedAt ?? o.CreatedAt)
            .ThenByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        if (ordenes.Count == 0)
        {
            return new PaginaDe<OrdenBloqueadaDto>(Array.Empty<OrdenBloqueadaDto>(), total);
        }

        // Consulta secundaria de partidas filtrada únicamente por las obras presentes en la página actual (#185, #210)
        var obraIdsPagina = ordenes.Select(o => o.ObraId).Distinct().ToList();
        var partidasPorObra = (await _context.PartidasPresupuestarias
                .AsNoTracking()
                .Where(partida => obraIdsPagina.Contains(partida.ObraId))
                .ToListAsync(cancellationToken))
            .GroupBy(partida => partida.ObraId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = ordenes.Select(oc =>
        {
            partidasPorObra.TryGetValue(oc.ObraId, out var partidasObra);
            var partida = EmparejadorPartidaPresupuestaria.BuscarPartida(partidasObra, oc.Etapa);
            return oc.ToBloqueadaDto(partida);
        }).ToList();

        return new PaginaDe<OrdenBloqueadaDto>(items, total);
    }
}
