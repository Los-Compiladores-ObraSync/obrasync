using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

namespace ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;

/// <summary>
/// Arma el seguimiento de una requisición: su licitación más reciente, las ofertas recibidas y las órdenes de compra.
/// Si todavía no hay licitación NO es un error (Compras puede no haberla publicado aún): se devuelve vacío.
/// </summary>
public class GetSeguimientoRequisicionQueryHandler
{
    private readonly ILicitacionRepository _licitaciones;
    private readonly ICotizacionRepository _cotizaciones;
    private readonly IOrdenCompraRepository _ordenes;

    public GetSeguimientoRequisicionQueryHandler(
        ILicitacionRepository licitaciones,
        ICotizacionRepository cotizaciones,
        IOrdenCompraRepository ordenes)
    {
        _licitaciones = licitaciones ?? throw new ArgumentNullException(nameof(licitaciones));
        _cotizaciones = cotizaciones ?? throw new ArgumentNullException(nameof(cotizaciones));
        _ordenes = ordenes ?? throw new ArgumentNullException(nameof(ordenes));
    }

    public async Task<SeguimientoRequisicionDto> Handle(
        GetSeguimientoRequisicionQuery query,
        CancellationToken cancellationToken = default)
    {
        var licitacion = await _licitaciones.GetMasRecientePorRequisicionAsync(query.RequisicionId, cancellationToken);
        if (licitacion is null)
        {
            return new SeguimientoRequisicionDto(
                query.RequisicionId, null, null, null, 0, Array.Empty<SeguimientoOrdenCompraDto>());
        }

        var cotizaciones = await _cotizaciones.GetByLicitacionIdAsync(licitacion.LicitacionId, cancellationToken);
        var ordenes = await _ordenes.GetByLicitacionIdsAsync(new[] { licitacion.LicitacionId }, cancellationToken);

        var nombrePorProveedor = cotizaciones
            .Where(c => !string.IsNullOrWhiteSpace(c.ProveedorNombre))
            .GroupBy(c => c.ProveedorId)
            .ToDictionary(g => g.Key, g => g.First().ProveedorNombre);

        var ordenesDto = ordenes
            .OrderBy(o => o.FechaEmision)
            .Select(o => new SeguimientoOrdenCompraDto(
                o.OrdenCompraId,
                o.Estado,
                o.FechaEmision,
                o.FechaDespacho,
                o.ProveedorId,
                nombrePorProveedor.GetValueOrDefault(o.ProveedorId),
                o.DespachadaPor,
                o.DespachadaEn,
                o.FechaEstimadaLlegada,
                o.Observacion,
                o.RecibidaCompleta,
                o.FechaRecepcionCompleta,
                o.TieneProblemaAbierto,
                o.MotivoProblemaAbierto,
                o.FechaProblemaAbierto,
                o.ProblemaResueltoEn,
                o.ProblemaResueltoPor))
            .ToList();

        return new SeguimientoRequisicionDto(
            query.RequisicionId,
            licitacion.LicitacionId,
            licitacion.Estado.ToString(),
            licitacion.FechaLimiteCotizacion,
            cotizaciones.Count,
            ordenesDto);
    }
}
