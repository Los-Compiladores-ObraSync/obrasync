using System.ComponentModel.DataAnnotations;

namespace ObraSync.Usuarios.Application.DTOs;

public record RecuperarPasswordRequestDto(
    [Required(ErrorMessage = "El email es obligatorio.")][EmailAddress(ErrorMessage = "El formato de email no es válido.")] string Email
);
