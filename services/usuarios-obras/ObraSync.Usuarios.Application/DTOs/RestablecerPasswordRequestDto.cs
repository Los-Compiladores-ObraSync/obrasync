using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs;

public record RestablecerPasswordRequestDto(
    [Required(ErrorMessage = "El email es obligatorio.")][EmailAddress(ErrorMessage = "El formato de email no es válido.")] string Email,
    [Required(ErrorMessage = "El token es obligatorio.")] string Token,
    [Required(ErrorMessage = "La contraseña es obligatoria.")][MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres.")] string NuevaPassword
);
