namespace ObraSync.Usuarios.Application.DTOs;

public record AuthResponseDto(
    Guid Id,
    string NombreCompleto,
    string Email,
    string Token,
    IEnumerable<string> Roles
);
