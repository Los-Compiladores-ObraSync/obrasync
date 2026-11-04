using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class CrearPlantillaObraRequestDto
    {
        [Required(ErrorMessage = "El nombre de la plantilla es obligatorio")]
        public string Nombre { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        // Lista ordenada de los nombres de las etapas que envía el frontend
        public List<string> Etapas { get; set; } = new List<string>();
    }
}