using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Mappings
{
    public static class InsumoMappingExtensions
    {
        public static InsumoCatalogoDto ToDto(this Insumo insumo) =>
            new()
            {
                ArticuloId = insumo.Id,
                Codigo = insumo.Codigo,
                Descripcion = insumo.Descripcion,
                UnidadMedida = insumo.UnidadMedida,
                Moneda = insumo.Moneda,
                PrecioReferencia = insumo.PrecioReferencia,
                Activo = insumo.Activo
            };
    }
}
