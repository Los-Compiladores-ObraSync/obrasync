using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Mappings;

public static class OrdenCompraMappingExtensions
{
    public static OrdenCompraResponseDto ToDto(this OrdenCompra ordenCompra)
    {
        ArgumentNullException.ThrowIfNull(ordenCompra);

        var fechaEmisionUtc = NormalizarAUtc(ordenCompra.FechaEmision);

        DateTime? fechaDespachoUtc = ordenCompra.FechaDespacho.HasValue
            ? NormalizarAUtc(ordenCompra.FechaDespacho.Value)
            : null;

        DateTime? fechaEstimadaLlegadaUtc = ordenCompra.FechaEstimadaLlegada.HasValue
            ? NormalizarAUtc(ordenCompra.FechaEstimadaLlegada.Value)
            : null;

        return new OrdenCompraResponseDto(
            Id: ordenCompra.OrdenCompraId,
            LicitacionId: ordenCompra.LicitacionId,
            CotizacionId: ordenCompra.CotizacionId,
            Estado: ordenCompra.Estado,
            FechaEmision: fechaEmisionUtc,
            ObraId: ordenCompra.ObraId,
            ProveedorId: ordenCompra.ProveedorId,
            FueDespachada: ordenCompra.FueDespachada,
            FechaDespacho: fechaDespachoUtc,
            DespachadaPor: ordenCompra.DespachadaPor,
            DespachadaEn: fechaDespachoUtc,
            FechaEstimadaLlegada: fechaEstimadaLlegadaUtc,
            Observacion: ordenCompra.Observacion);
    }

    private static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}
