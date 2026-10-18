using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class CrearObraDto
    {
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public Guid CapatazId { get; set; } // ID del usuario con rol Capataz asignado
        public List<string> FasesEtapas { get; set; } = new List<string>(); // Ej: ["Cimientos", "Estructura"]
    }
}
