using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class AuthControllerTests
{
    private readonly IAuthService _authService;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _authService = Substitute.For<IAuthService>();
        _controller = new AuthController(_authService);
    }

    [Fact]
    public async Task RecuperarPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        // Arrange
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var respuesta = new MensajeResponseDto(
            "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.");
        _authService.RecuperarPasswordAsync(request).Returns(respuesta);

        // Act
        var result = await _controller.RecuperarPassword(request);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }
}
