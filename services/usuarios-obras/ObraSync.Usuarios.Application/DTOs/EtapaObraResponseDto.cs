using System;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class EtapaObraResponseDto
    {
        public Guid Id { get; set; }
        public string NombreEtapa { get; set; } = string.Empty;
        public string Estado { get; set; } = "Pendiente";
    }
}
