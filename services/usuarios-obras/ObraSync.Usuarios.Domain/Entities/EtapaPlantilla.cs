using System;

namespace ObraSync.Usuarios.Domain.Entities
{
    public class EtapaPlantilla
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string NombreEtapa { get; set; } = string.Empty;
        public int Orden { get; set; } // Garantiza la lista ordenada de etapas

        // Relación con la Plantilla padre
        public Guid PlantillaObraId { get; set; }
        public PlantillaObra? PlantillaObra { get; set; }
    }
}