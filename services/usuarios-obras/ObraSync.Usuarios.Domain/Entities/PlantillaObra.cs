using System;
using System.Collections.Generic;

namespace ObraSync.Usuarios.Domain.Entities
{
    public class PlantillaObra
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;

        // Lista ordenada de etapas asociadas a la plantilla
        public ICollection<EtapaPlantilla> Etapas { get; set; } = new List<EtapaPlantilla>();
    }
}