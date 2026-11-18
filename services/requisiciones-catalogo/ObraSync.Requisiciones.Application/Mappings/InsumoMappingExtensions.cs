using System;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.UseCases.Catalog.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Mappings
{
    public static class InsumoMappingExtensions
    {
        public static InsumoCatalogoDto ToDto(this Insumo insumo)
        {
            ArgumentNullException.ThrowIfNull(insumo);

            return new InsumoCatalogoDto
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

        public static CreateInsumoCommand ToCommand(this CrearInsumoRequestDto request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new CreateInsumoCommand(
                request.Codigo,
                request.Descripcion,
                request.UnidadMedida,
                request.Activo,
                request.PrecioReferencia,
                request.Moneda);
        }
    }
}
