using System;
using System.Collections.Generic;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class RequisicionResponseDto
    {
        public Guid RequisicionId { get; set; }
        public string NumeroRequisicion { get; set; } = string.Empty;
        public Guid ObraId { get; set; }
        public string? Etapa { get; set; }
        public Guid SolicitanteId { get; set; }
        public Guid? AprobadorId { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string? Observaciones { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaAprobacion { get; set; }
        public List<RequisicionItemDto> Items { get; set; } = new();
    }
}
