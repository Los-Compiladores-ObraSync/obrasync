using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs;

public record RegisterRequestDto(
    [Required(ErrorMessage = "El nombre completo es obligatorio.")] string NombreCompleto,
    [Required(ErrorMessage = "El email es obligatorio.")][EmailAddress(ErrorMessage = "El formato de email no es válido.")] string Email,
    [Required(ErrorMessage = "La contraseña es obligatoria.")][MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")] string Password,
    [Required(ErrorMessage = "El rol es obligatorio.")] string Rol
);