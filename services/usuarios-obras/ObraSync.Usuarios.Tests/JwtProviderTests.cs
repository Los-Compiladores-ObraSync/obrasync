using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
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

        // Act - Ejecutamos la generación del token para forzar la validación de la clave
        Action act = () => provider.GenerarToken(usuario, new[] { UserRoles.Admin });

        // Assert
        act.Should().Throw<Exception>();
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
        tokenString.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        // Validar claims
        jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value.Should().Be(usuario.Id.ToString());
        jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value.Should().Be(usuario.Email);
        jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Name)?.Value.Should().Be(usuario.NombreCompleto);
        jwtToken.Claims.FirstOrDefault(c => c.Type == "role")?.Value.Should().Be(UserRoles.Capataz);

        // El token es liviano, como pide el contrato: sin Issuer, Audience ni Jti
        jwtToken.Issuer.Should().BeNullOrEmpty();
        jwtToken.Audiences.Should().BeEmpty();
        jwtToken.Claims.Should().NotContain(c => c.Type == JwtRegisteredClaimNames.Jti);

        // Vence a los 60 minutos
        jwtToken.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes), TimeSpan.FromMinutes(1));
    }
}