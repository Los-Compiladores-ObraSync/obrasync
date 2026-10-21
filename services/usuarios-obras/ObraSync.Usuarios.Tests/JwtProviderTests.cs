using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Services;
using ObraSync.Usuarios.Infrastructure.Settings;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class JwtProviderTests
{
    private readonly JwtSettings _jwtSettings;

    public JwtProviderTests()
    {
        _jwtSettings = new JwtSettings
        {
            Secret = "SuperSecretKeyForTestingJwtProvider2026!",
            ExpirationMinutes = 60,
            Issuer = "obrasync-usuarios",
            Audience = "obrasync-services"
        };
    }

    [Fact]
    public void GenerarToken_DeberiaLanzarExcepcion_CuandoSecretEsInvalido()
    {
        // Arrange
        var settingsInvalidos = Options.Create(new JwtSettings { Secret = "Corta" }); // Menor a 32 caracteres
        var provider = new JwtProvider(settingsInvalidos);
        var usuario = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "test@obrasync.com",
            NombreCompleto = "Usuario Prueba"
        };

        // Act + Assert: ejecutamos la generación del token para forzar la validación de la clave
        Assert.ThrowsAny<Exception>(() => provider.GenerarToken(usuario, new[] { UserRoles.Admin }));
    }

    [Fact]
    public void GenerarToken_DeberiaIncluirClaimsRequeridosYExpiracionCorrecta()
    {
        // Arrange
        var options = Options.Create(_jwtSettings);
        var provider = new JwtProvider(options);

        var usuario = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "capataz@obrasync.com",
            NombreCompleto = "Juan Capataz"
        };
        var roles = new[] { UserRoles.Capataz };

        // Act
        var tokenString = provider.GenerarToken(usuario, roles);

        // Assert
        Assert.False(string.IsNullOrEmpty(tokenString));

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        string? Claim(string tipo) => jwtToken.Claims.FirstOrDefault(c => c.Type == tipo)?.Value;

        // Claims del contrato
        Assert.Equal(usuario.Id.ToString(), Claim(JwtRegisteredClaimNames.Sub));
        Assert.Equal(usuario.Email, Claim(JwtRegisteredClaimNames.Email));
        Assert.Equal(usuario.NombreCompleto, Claim(JwtRegisteredClaimNames.Name));
        Assert.Equal(UserRoles.Capataz, Claim("role"));

        // El token es liviano, como pide el contrato: sin Issuer, Audience ni Jti
        Assert.True(string.IsNullOrEmpty(jwtToken.Issuer));
        Assert.Empty(jwtToken.Audiences);
        Assert.DoesNotContain(jwtToken.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);

        // Vence a los 60 minutos (con un minuto de margen)
        var esperado = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes);
        Assert.InRange(jwtToken.ValidTo, esperado.AddMinutes(-1), esperado.AddMinutes(1));
    }
}