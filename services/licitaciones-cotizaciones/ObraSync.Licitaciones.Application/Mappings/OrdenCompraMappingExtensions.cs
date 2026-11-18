using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Mappings;

public static class OrdenCompraMappingExtensions
{
    public static OrdenCompraResponseDto ToDto(this OrdenCompra ordenCompra)
    {
        ArgumentNullException.ThrowIfNull(ordenCompra);

        var fechaEmisionUtc = ordenCompra.FechaEmision.Kind == DateTimeKind.Utc
            ? ordenCompra.FechaEmision
            : DateTime.SpecifyKind(ordenCompra.FechaEmision, DateTimeKind.Utc);

        DateTime? fechaDespachoUtc = ordenCompra.FechaDespacho.HasValue
            ? (ordenCompra.FechaDespacho.Value.Kind == DateTimeKind.Utc
                ? ordenCompra.FechaDespacho.Value
                : DateTime.SpecifyKind(ordenCompra.FechaDespacho.Value, DateTimeKind.Utc))
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
            FechaDespacho: fechaDespachoUtc);
    }
}
