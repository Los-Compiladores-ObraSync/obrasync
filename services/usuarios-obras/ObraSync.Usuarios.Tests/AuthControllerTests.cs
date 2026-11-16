using System.Security.Claims;
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
        _controller = new AuthController(_authService)
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

    [Fact]
    public async Task RestablecerPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto(
            "capataz@obrasync.com",
            "CfDJ8+token-valido==",
            "NuevaClave123");
        var respuesta = new MensajeResponseDto("Contraseña restablecida con éxito.");
        _authService.RestablecerPasswordAsync(request).Returns(respuesta);

        // Act
        var result = await _controller.RestablecerPassword(request);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }

    [Fact]
    public async Task CambiarPassword_DebeRetornar200ConElMensajeDelServicio()
    {
        // Arrange
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");
        var respuesta = new MensajeResponseDto("Contraseña actualizada con éxito.");
        _authService.CambiarPasswordAsync(request, Arg.Any<ClaimsPrincipal>()).Returns(respuesta);

        // Act
        var result = await _controller.CambiarPassword(request);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }

    [Fact]
    public async Task GetCapatacesActivos_PaginacionValida_Retorna200YHeaders()
    {
        // Arrange (#182)
        var items = new List<CapatazDto>
        {
            new(Guid.NewGuid(), "Ana Capataz"),
            new(Guid.NewGuid(), "Carlos Capataz")
        };
        _authService.GetCapatacesActivosPaginadosAsync(new ParametrosPagina(1, 2))
            .Returns((items, 5));

        // Act
        var result = await _controller.GetCapatacesActivos(pagina: 1, tamano: 2);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(items, ok.Value);
        Assert.Equal("5", _controller.Response.Headers["X-Total-Count"].ToString());
        Assert.Equal("1", _controller.Response.Headers["X-Page"].ToString());
        Assert.Equal("2", _controller.Response.Headers["X-Page-Size"].ToString());
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetCapatacesActivos_ParametrosInvalidos_Retorna400ProblemDetails(int pagina, int tamano)
    {
        // Act (#182)
        var result = await _controller.GetCapatacesActivos(pagina, tamano);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Paginación inválida", problem.Title);
    }
}
