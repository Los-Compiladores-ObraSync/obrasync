using System;

namespace ObraSync.Usuarios.Domain.Entities
{
    public class EtapaObra
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string NombreEtapa { get; set; } = string.Empty; // Ej: Cimientos, Estructura, Mampostería
        public string Estado { get; set; } = "Pendiente";

        // Clave foránea y relación con la Obra
        public Guid ObraId { get; set; }
        public Obra? Obra { get; set; }
    }
}