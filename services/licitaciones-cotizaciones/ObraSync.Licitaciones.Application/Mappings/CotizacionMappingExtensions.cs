using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Mappings;

public static class CotizacionMappingExtensions
{
    public static CotizacionResponseDto ToDto(this Cotizacion cotizacion)
    {
        return new CotizacionResponseDto
        {
            CotizacionId = cotizacion.CotizacionId,
            LicitacionId = cotizacion.LicitacionId,
            ProveedorId = cotizacion.ProveedorId,
            Flete = cotizacion.Flete,
            PlazoEntregaDias = cotizacion.PlazoEntregaDias,
            FechaCreacion = DateTime.SpecifyKind(cotizacion.FechaCreacion, DateTimeKind.Utc),
            Items = cotizacion.Items
                .Select(i => new CrearCotizacionItemDto
                {
                    ArticuloId = i.ArticuloId,
                    PrecioUnitario = i.PrecioUnitario
                })
                .ToList()
        };
    }
}
