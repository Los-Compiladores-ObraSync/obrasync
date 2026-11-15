using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs;

public record CambiarPasswordRequestDto(
    [Required(ErrorMessage = "La contraseña actual es obligatoria.")] string PasswordActual,
    [Required(ErrorMessage = "La nueva contraseña es obligatoria.")][MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")] string NuevaPassword,
    [Required(ErrorMessage = "La confirmación de la contraseña es obligatoria.")] string ConfirmarPassword
);
