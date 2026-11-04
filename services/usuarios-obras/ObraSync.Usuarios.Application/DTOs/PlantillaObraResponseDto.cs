using System;
using System.Collections.Generic;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class PlantillaObraResponseDto
    {
        public Guid Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public List<EtapaPlantillaDto> Etapas { get; set; } = new List<EtapaPlantillaDto>();
    }

    public class EtapaPlantillaDto
    {
        public Guid Id { get; set; }
        public string NombreEtapa { get; set; } = string.Empty;
        public int Orden { get; set; }
    }
}