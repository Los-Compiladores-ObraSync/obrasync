using System.Collections.Generic;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class EditarRequisicionRequestDto
    {
        public string? Etapa { get; set; }
        public string? Observaciones { get; set; }
        public List<CrearRequisicionItemDto> Items { get; set; } = new();
    }
}
