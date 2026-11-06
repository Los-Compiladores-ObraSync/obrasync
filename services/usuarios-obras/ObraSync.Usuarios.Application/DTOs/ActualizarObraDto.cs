using System;
using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs
{
    public class ActualizarObraDto
    {
        [Required(ErrorMessage = "El nombre de la obra es obligatorio.")]
        public string Nombre { get; set; } = string.Empty;

        public string Ubicacion { get; set; } = string.Empty;

        public DateTime? FechaInicio { get; set; }

        [Required(ErrorMessage = "El ID del capataz es obligatorio.")]
        public Guid CapatazId { get; set; }
    }
}
