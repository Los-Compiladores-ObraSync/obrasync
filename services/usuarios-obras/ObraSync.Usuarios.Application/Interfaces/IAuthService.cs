using System.Security.Claims;
using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegistrarAsync(RegisterRequestDto request, ClaimsPrincipal? ejecutor = null);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request);
    Task<IEnumerable<CapatazDto>> GetCapatacesActivosAsync();
    Task<MensajeResponseDto> RecuperarPasswordAsync(RecuperarPasswordRequestDto request);
    Task<MensajeResponseDto> RestablecerPasswordAsync(RestablecerPasswordRequestDto request);
    Task<MensajeResponseDto> CambiarPasswordAsync(CambiarPasswordRequestDto request, ClaimsPrincipal ejecutor);
}
