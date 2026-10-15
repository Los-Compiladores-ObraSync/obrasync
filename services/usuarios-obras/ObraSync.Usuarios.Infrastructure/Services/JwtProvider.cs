using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Settings;

namespace ObraSync.Usuarios.Infrastructure.Services;

public class JwtProvider : IJwtProvider
{
    private readonly JwtSettings _jwtSettings;

    public JwtProvider(IOptions<JwtSettings> jwtOptions)
    {
        _jwtSettings = jwtOptions.Value;
    }

    public string GenerarToken(AppUser usuario, IEnumerable<string> roles)
    {
        // 1. Claims según el contrato de ObraSync: sub, email, name, jti
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Name, usuario.NombreCompleto),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // 2. Agregar los roles usando la clave literal "role"
        foreach (var role in roles)
        {
            claims.Add(new Claim("role", role));
        }

        // 3. Crear firma simétrica HS256
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // 4. Armar el token
        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}