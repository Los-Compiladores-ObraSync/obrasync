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

    public async Task<List<OrdenPendienteDto>> Handle(
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

        var ordenes = await dbQuery.ToListAsync(cancellationToken);

        var ordenesIds = ordenes.Select(o => o.Id).ToList();

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

        return ordenes
            .Select(o => new OrdenPendienteDto
            {
                OrdenCompraId = o.Id,
                ObraId = o.ObraId,
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
    }
}
