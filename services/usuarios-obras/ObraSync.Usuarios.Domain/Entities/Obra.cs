using System;
using System.Collections.Generic;

namespace ObraSync.Usuarios.Domain.Entities
{
    public class Obra
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; } = DateTime.UtcNow;

        // Relación con el responsable (Capataz) usando Guid o string según el ID de AppUser
        public Guid CapatazId { get; set; }
        public AppUser? Capataz { get; set; }

        // Relación con las etapas constructivas
        public ICollection<EtapaObra> Etapas { get; set; } = new List<EtapaObra>();
    }
}