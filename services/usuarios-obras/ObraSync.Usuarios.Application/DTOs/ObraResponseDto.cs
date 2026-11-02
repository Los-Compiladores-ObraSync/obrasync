using System;
using System.Collections.Generic;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class ObraResponseDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public Guid CapatazId { get; set; }
        public string? Capataz { get; set; }
        public List<EtapaObraResponseDto> Etapas { get; set; } = new();
    }
}
