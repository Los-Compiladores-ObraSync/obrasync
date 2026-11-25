using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using ObraSync.Usuarios.Api.Controllers;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
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
    public async Task Registrar_SinCabeceraAuthorization_DebeRetornar201Created()
    {
        var request = new RegisterRequestDto("Proveedor S.A.", "prov@obrasync.com", "Password123!", UserRoles.Proveedor);
        var respuesta = new AuthResponseDto(
            Guid.NewGuid(),
            "Proveedor S.A.",
            "prov@obrasync.com",
            "jwt-token",
            new[] { UserRoles.Proveedor });

        _authService.RegistrarAsync(request, Arg.Any<ClaimsPrincipal>()).Returns(respuesta);

        var result = await _controller.Registrar(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(respuesta, created.Value);
    }

    [Fact]
    public async Task Registrar_ConCabeceraAuthorizationYUsuarioNoAutenticado_DebeLanzarUnauthorizedAccessException()
    {
        var request = new RegisterRequestDto("Proveedor S.A.", "prov@obrasync.com", "Password123!", UserRoles.Proveedor);
        _controller.HttpContext.Request.Headers.Authorization = "Bearer token-invalido";

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.Registrar(request));

        Assert.Equal("El token proporcionado es inválido o ha expirado.", ex.Message);
        await _authService.DidNotReceiveWithAnyArgs().RegistrarAsync(default!, default);
    }

    [Fact]
    public async Task Login_CredencialesValidas_DebeRetornar200Ok()
    {
        var request = new LoginRequestDto("admin@obrasync.com", "Password123!");
        var respuesta = new AuthResponseDto(
            Guid.NewGuid(),
            "Admin Sistema",
            "admin@obrasync.com",
            "jwt-token",
            new[] { UserRoles.Admin });

        _authService.LoginAsync(request).Returns(respuesta);

        var result = await _controller.Login(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Same(respuesta, ok.Value);
    }
}
