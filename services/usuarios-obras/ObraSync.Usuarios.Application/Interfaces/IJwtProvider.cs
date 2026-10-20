using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface IJwtProvider
{
    string GenerarToken(AppUser usuario, IEnumerable<string> roles);
}