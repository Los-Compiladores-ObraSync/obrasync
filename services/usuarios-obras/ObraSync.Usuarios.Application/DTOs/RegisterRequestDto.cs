using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs;

public record RegisterRequestDto(
    [Required(ErrorMessage = "El nombre completo es obligatorio.")] string NombreCompleto,
    [Required(ErrorMessage = "El email es obligatorio.")][EmailAddress(ErrorMessage = "El formato de email no es válido.")] string Email,
    [Required(ErrorMessage = "La contraseña es obligatoria.")][MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")] string Password,
    [Required(ErrorMessage = "El rol es obligatorio.")] string Rol,
    // Opcional: sirve para mandarle por WhatsApp los enlaces de cotización a los proveedores.
    [RegularExpression(@"^\+?[0-9 ()\-]{8,20}$", ErrorMessage = "El teléfono solo puede tener números, espacios, guiones, paréntesis y un + al inicio (entre 8 y 20 caracteres).")] string? Telefono = null
);