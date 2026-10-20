using Microsoft.AspNetCore.Identity;

namespace ObraSync.Usuarios.Domain.Entities;

// Hereda de IdentityUser para agregar propiedades adicionales
// Representa a los usuarios en la BD
public class AppUser : IdentityUser<Guid>
{
public string NombreCompleto { get; set; } = string.Empty;
public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
public bool Activo { get; set; } = true;
}