using System.Security.Claims;
using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IPasswordService
{
    Task<MensajeResponseDto> RecuperarPasswordAsync(RecuperarPasswordRequestDto request);
    Task<MensajeResponseDto> RestablecerPasswordAsync(RestablecerPasswordRequestDto request);
    Task<MensajeResponseDto> CambiarPasswordAsync(CambiarPasswordRequestDto request, ClaimsPrincipal ejecutor);
}
