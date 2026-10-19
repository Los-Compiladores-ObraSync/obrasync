using System.Linq;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Mappings
{
    public static class RequisicionMappingExtensions
    {
        public static RequisicionResponseDto ToDto(this Requisicion requisicion) =>
            new()
            {
                RequisicionId = requisicion.Id,
                NumeroRequisicion = requisicion.NumeroRequisicion,
                ObraId = requisicion.ObraId,
                Etapa = requisicion.Etapa,
                SolicitanteId = requisicion.SolicitanteId,
                AprobadorId = requisicion.AprobadorId,
                Estado = requisicion.Estado,
                Observaciones = requisicion.Observaciones,
                FechaCreacion = requisicion.FechaCreacion,
                FechaAprobacion = requisicion.FechaAprobacion,
                Items = requisicion.Items.Select(i => new RequisicionItemDto
                {
                    ArticuloId = i.ArticuloId,
                    Descripcion = i.Descripcion,
                    Cantidad = i.Cantidad,
                    UnidadMedida = i.UnidadMedida
                }).ToList()
            };
    }
}
