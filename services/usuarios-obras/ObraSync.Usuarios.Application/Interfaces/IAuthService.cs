using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto> RegistrarAsync(RegisterRequestDto request);
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request);
    Task<IEnumerable<CapatazDto>> GetCapatacesActivosAsync();
    Task<MensajeResponseDto> RecuperarPasswordAsync(RecuperarPasswordRequestDto request);
    Task<MensajeResponseDto> RestablecerPasswordAsync(RestablecerPasswordRequestDto request);
}