using Microsoft.AspNetCore.Identity;
using NSubstitute;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class AuthServiceTests
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IJwtProvider _jwtProvider;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(store, null, null, null, null, null, null, null, null);
        _jwtProvider = Substitute.For<IJwtProvider>();

        _authService = new AuthService(_userManager, _jwtProvider);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarArgumentException_CuandoRolEsInvalido()
    {
        // Arrange
        var request = new RegisterRequestDto("Juan Perez", "juan@test.com", "Password123!", "RolInexistente");

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Contains("no es válido", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarForbiddenException_CuandoRolEsAdmin()
    {
        // Arrange
        var request = new RegisterRequestDto("Juan Admin", "admin@test.com", "Password123!", UserRoles.Admin);

        // Act
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Contains("No está permitido registrar cuentas con rol Admin", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarInvalidOperationException_CuandoEmailYaExiste()
    {
        // Arrange
        var request = new RegisterRequestDto("Juan Perez", "existente@test.com", "Password123!", UserRoles.Capataz);
        _userManager.FindByEmailAsync(request.Email)
            .Returns(Task.FromResult<AppUser?>(new AppUser { Email = request.Email }));

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Contains("Ya existe un usuario", ex.Message);
    }

    [Fact]
    public async Task GetCapatacesActivosAsync_DebeRetornarSoloCapatacesActivos()
    {
        // Arrange
        var capataces = new List<AppUser>
        {
            new AppUser { Id = Guid.NewGuid(), NombreCompleto = "Carlos Capataz", Activo = true },
            new AppUser { Id = Guid.NewGuid(), NombreCompleto = "Inactivo Capataz", Activo = false }
        };

        _userManager.GetUsersInRoleAsync(UserRoles.Capataz).Returns(capataces);

        // Act
        var result = await _authService.GetCapatacesActivosAsync();

        // Assert
        var unico = Assert.Single(result);
        Assert.Equal("Carlos Capataz", unico.NombreCompleto);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaRetornarAuthResponseDto_CuandoRegistroEsExitoso()
    {
        // Arrange
        var request = new RegisterRequestDto("Juan Capataz", "capataz@test.com", "Password123!", UserRoles.Capataz);

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        _userManager.CreateAsync(Arg.Any<AppUser>(), request.Password)
            .Returns(Task.FromResult(IdentityResult.Success));

        // Se agrega el mock de AddToRoleAsync
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        _userManager.GetRolesAsync(Arg.Any<AppUser>())
            .Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Capataz }));

        _jwtProvider.GenerarToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>())
            .Returns("fake-jwt-token");

        // Act
        var result = await _authService.RegistrarAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Email, result.Email);
        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Contains(UserRoles.Capataz, result.Roles);
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarUnauthorizedAccessException_CuandoUsuarioNoExisteOEstaInactivo()
    {
        // Arrange
        var request = new LoginRequestDto("invalido@test.com", "Password123!");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert
        Assert.Contains("Credenciales inválidas", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarUnauthorizedAccessException_CuandoPasswordEsIncorrecta()
    {
        // Arrange
        var request = new LoginRequestDto("usuario@test.com", "WrongPassword");
        var user = new AppUser { Email = request.Email, Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.CheckPasswordAsync(user, request.Password).Returns(Task.FromResult(false));

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert
        Assert.Contains("Credenciales inválidas", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_DeberiaRetornarAuthResponseDto_CuandoCredencialesSonCorrectas()
    {
        // Arrange
        var request = new LoginRequestDto("usuario@test.com", "Password123!");
        var user = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Juan Usuario", Activo = true };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.CheckPasswordAsync(user, request.Password).Returns(Task.FromResult(true));
        _userManager.GetRolesAsync(user).Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Admin }));
        _jwtProvider.GenerarToken(user, Arg.Any<IEnumerable<string>>()).Returns("fake-jwt-token");

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Email, result.Email);
        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Contains(UserRoles.Admin, result.Roles);
    }
}