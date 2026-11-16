using System;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class HistorialRechazoRequisicionDto
    {
        public Guid Id { get; set; }
        public Guid? RechazadorId { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public DateTime FechaRechazo { get; set; }
    }
}
