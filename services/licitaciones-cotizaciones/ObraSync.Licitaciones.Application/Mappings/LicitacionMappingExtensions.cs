using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Mappings;

public static class LicitacionMappingExtensions
{
    public static LicitacionResponseDto ToDto(this Licitacion licitacion)
    {
        return new LicitacionResponseDto
        {
            LicitacionId = licitacion.LicitacionId,
            RequisicionId = licitacion.RequisicionId,
            NumeroRequisicion = licitacion.NumeroRequisicion,
            ObraId = licitacion.ObraId,
            Estado = licitacion.Estado.ToString(),
            FechaCreacion = DateTime.SpecifyKind(licitacion.FechaCreacion, DateTimeKind.Utc),
            FechaLimiteCotizacion = DateTime.SpecifyKind(licitacion.FechaLimiteCotizacion, DateTimeKind.Utc),
            Items = licitacion.Items
                .Select(i => new PliegoItemDto
                {
                    ArticuloId = i.ArticuloId,
                    Descripcion = i.Descripcion,
                    Cantidad = i.Cantidad,
                    UnidadMedida = i.UnidadMedida
                })
                .ToList()
        };
    }
}
