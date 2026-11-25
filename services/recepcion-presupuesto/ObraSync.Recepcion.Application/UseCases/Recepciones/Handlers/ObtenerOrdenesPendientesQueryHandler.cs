using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Constants;
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
                        o.FechaDespacho != null &&
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

        // Consulta secundaria de recepciones válidas (excluyendo Rechazada, #217) filtrada por los IDs de la página actual (#185)
        var recibidos = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId) &&
                        r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles.Select(d => new
            {
                r.OrdenCompraId,
                d.ArticuloId,
                d.CantidadRecibida
            }))
            .ToListAsync(cancellationToken);

        // Estado de la última recepción registrada por orden para informar si hay un problema abierto (#217)
        var ultimosEstados = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId))
            .Select(r => new
            {
                r.OrdenCompraId,
                r.Estado,
                r.CreatedAt,
                r.FechaRecepcion,
                r.Id
            })
            .ToListAsync(cancellationToken);

        var ultimaPorOrden = ultimosEstados
            .GroupBy(r => r.OrdenCompraId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CreatedAt)
                      .ThenByDescending(x => x.FechaRecepcion)
                      .ThenByDescending(x => x.Id)
                      .First());

        var ordenesConEntregaParcial = ultimosEstados
            .Where(r => r.Estado == EstadoRecepcion.Parcial)
            .Select(r => r.OrdenCompraId)
            .ToHashSet();

        var items = ordenes
            .Select(o =>
            {
                var recibidosDeEstaOrden = recibidos
                    .Where(d => d.OrdenCompraId == o.Id)
                    .GroupBy(d => d.ArticuloId)
                    .ToDictionary(g => g.Key, g => g.Sum(d => d.CantidadRecibida));

                var saldosPendientes = o.CalcularSaldosPendientes(recibidosDeEstaOrden);
                // El problema sigue "abierto" hasta que el proveedor vuelve a despachar (ese redespacho reabre la entrega al capataz).
                bool tieneProblemaAbierto = ultimaPorOrden.TryGetValue(o.Id, out var ultima)
                    && ultima.Estado == EstadoRecepcion.Rechazada
                    && o.EsperaNuevoDespachoTras(ultima.CreatedAt);
                bool tieneEntregaParcial = ordenesConEntregaParcial.Contains(o.Id);

                return new OrdenPendienteDto
                {
                    OrdenCompraId = o.Id,
                    ObraId = o.ObraId,
                    Etapa = o.Etapa,
                    SinPartidaPresupuestaria = o.SinPartidaPresupuestaria,
                    FechaDespacho = FechaUtc.Normalizar(o.FechaDespacho),
                    FechaEstimadaLlegada = FechaUtc.Normalizar(o.FechaEstimadaLlegada),
                    ObservacionDespacho = o.ObservacionDespacho,
                    TieneProblemaAbierto = tieneProblemaAbierto,
                    EsperaNuevoDespacho = tieneProblemaAbierto,
                    EstadoEntrega = EstadosEntregaOrden.Calcular(
                        estaCompleta: false,
                        tieneProblemaAbierto: tieneProblemaAbierto,
                        tieneRecepcionParcial: tieneEntregaParcial),
                    Items = o.Items
                        .Select(i =>
                        {
                            decimal recibido = recibidosDeEstaOrden.GetValueOrDefault(i.ArticuloId, 0m);
                            decimal pendiente = saldosPendientes.GetValueOrDefault(i.ArticuloId, Math.Max(0m, i.Cantidad - recibido));

                            return new ItemOrdenPendienteDto
                            {
                                ArticuloId = i.ArticuloId,
                                DescripcionArticulo = i.Descripcion ?? string.Empty,
                                CantidadEsperada = i.Cantidad,
                                CantidadRecibida = recibido,
                                CantidadPendiente = pendiente,
                                UnidadMedida = i.UnidadMedida
                            };
                        })
                        .ToList()
                };
            })
            .ToList();

        return new PaginaDe<OrdenPendienteDto>(items, total);
    }
}
