using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class PasswordControllerTests
{
    private readonly IPasswordService _passwordService;
    private readonly PasswordController _controller;

    public PasswordControllerTests()
    {
        _passwordService = Substitute.For<IPasswordService>();
        _controller = new PasswordController(_passwordService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task RecuperarPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var respuesta = new MensajeResponseDto(
            "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.");
        _passwordService.RecuperarPasswordAsync(request).Returns(respuesta);

        var result = await _controller.RecuperarPassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }

    [Fact]
    public async Task RestablecerPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        var request = new RestablecerPasswordRequestDto(
            "capataz@obrasync.com",
            "CfDJ8+token-valido==",
            "NuevaClave123");
        var respuesta = new MensajeResponseDto("Contraseña restablecida con éxito.");
        _passwordService.RestablecerPasswordAsync(request).Returns(respuesta);

        var result = await _controller.RestablecerPassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }

    [Fact]
    public async Task CambiarPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");
        var respuesta = new MensajeResponseDto("Contraseña actualizada con éxito.");
        _passwordService.CambiarPasswordAsync(request, Arg.Any<ClaimsPrincipal>()).Returns(respuesta);

        var result = await _controller.CambiarPassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }
}
