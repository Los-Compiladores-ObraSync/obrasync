using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

public class ObtenerOrdenesPendientesQueryHandler
{
    private readonly IRecepcionDbContext _context;

    public ObtenerOrdenesPendientesQueryHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<OrdenPendienteDto>> Handle(
        ObtenerOrdenesPendientesQuery query,
        CancellationToken cancellationToken = default)
    {
        var dbQuery = _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .Where(o => o.Estado == EstadoOrdenCompra.Activa &&
                        !_context.Recepciones.Any(
                            r => r.OrdenCompraId == o.Id &&
                                 r.Estado == EstadoRecepcion.Completa));

        if (query.ObraId.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.ObraId == query.ObraId.Value);
        }
        else if (query.ObrasPermitidas != null)
        {
            dbQuery = dbQuery.Where(o => query.ObrasPermitidas.Contains(o.ObraId));
        }

        if (query.SinPartida.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.SinPartidaPresupuestaria == query.SinPartida.Value);
        }

        // Total sobre la consulta base con los mismos filtros sin paginar (#185)
        var total = await dbQuery.CountAsync(cancellationToken);

        var p = query.Paginacion ?? new ParametrosPagina();
        var ordenes = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .ToListAsync(cancellationToken);

        var ordenesIds = ordenes.Select(o => o.Id).ToList();

        // Consulta secundaria de recepciones filtrada ÚNICAMENTE por los IDs de la página actual (#185)
        var recibidos = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId))
            .SelectMany(r => r.Detalles.Select(d => new
            {
                r.OrdenCompraId,
                d.ArticuloId,
                d.CantidadRecibida
            }))
            .ToListAsync(cancellationToken);

        var items = ordenes
            .Select(o => new OrdenPendienteDto
            {
                OrdenCompraId = o.Id,
                ObraId = o.ObraId,
                Etapa = o.Etapa,
                SinPartidaPresupuestaria = o.SinPartidaPresupuestaria,
                Items = o.Items
                    .Select(i => new ItemOrdenPendienteDto
                    {
                        ArticuloId = i.ArticuloId,
                        DescripcionArticulo = i.Descripcion ?? string.Empty,
                        CantidadEsperada = i.Cantidad,
                        CantidadRecibida = recibidos
                            .Where(d => d.OrdenCompraId == o.Id && d.ArticuloId == i.ArticuloId)
                            .Sum(d => d.CantidadRecibida),
                        UnidadMedida = i.UnidadMedida
                    })
                    .ToList()
            })
            .ToList();

        return new PaginaDe<OrdenPendienteDto>(items, total);
    }
}
