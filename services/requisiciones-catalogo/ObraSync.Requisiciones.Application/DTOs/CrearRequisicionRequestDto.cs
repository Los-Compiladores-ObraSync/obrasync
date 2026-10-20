using System;
using System.Collections.Generic;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class CrearRequisicionRequestDto
    {
        public Guid ObraId { get; set; }
        public string? Etapa { get; set; }
        public string? Observaciones { get; set; }
        public List<CrearRequisicionItemDto> Items { get; set; } = new();
    }
}
