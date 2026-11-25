using System.Security.Claims;
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
    private const string MensajeCredencialesInvalidas =
        "Credenciales inválidas.";

    private const string MensajeCuentaBloqueada =
        "La cuenta está bloqueada temporalmente por múltiples intentos fallidos. Intentá nuevamente más tarde.";

    private const string MensajePasswordCorta =
        "La contraseña debe tener al menos 8 caracteres.";

    private const string MensajePasswordSinDigito =
        "La contraseña debe incluir al menos un número.";

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

    // Arma un ClaimsPrincipal autenticado usando roleType: "role", igual que Program.cs en producción
    private static ClaimsPrincipal CrearEjecutorConRol(string rol, Guid? userId = null)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
                new Claim("name", $"Usuario {rol}"),
                new Claim("role", rol)
            },
            authenticationType: "Test",
            nameType: "name",
            roleType: "role");

        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarArgumentException_CuandoRolEsInvalido()
    {
        // Arrange
        var request = new RegisterRequestDto("Juan Perez", "juan@test.com", "Password123!", "RolInexistente");

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Contains("no es válido", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarForbiddenException_CuandoRolEsAdmin()
    {
        // Arrange: ni anónimo ni un propio Admin pueden registrar otra cuenta Admin
        var request = new RegisterRequestDto("Juan Admin", "admin@test.com", "Password123!", UserRoles.Admin);
        var ejecutorAdmin = CrearEjecutorConRol(UserRoles.Admin);

        // Act
        var exAnonimo = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request));
        var exAdmin = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request, ejecutorAdmin));

        // Assert
        Assert.Contains("No está permitido registrar cuentas con rol Admin", exAnonimo.Message);
        Assert.Contains("No está permitido registrar cuentas con rol Admin", exAdmin.Message);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaLanzarInvalidOperationException_CuandoEmailYaExiste()
    {
        // Arrange: usa Proveedor para pasar el chequeo de registro público (#117) y llegar a la validación de email duplicado
        var request = new RegisterRequestDto("Juan Proveedor", "existente@test.com", "Password123!", UserRoles.Proveedor);
        _userManager.FindByEmailAsync(request.Email)
            .Returns(Task.FromResult<AppUser?>(new AppUser { Email = request.Email }));

        // Act
        var ex = await Assert.ThrowsAsync<ConflictoException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Contains("Ya existe un usuario", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_DeberiaRetornarAuthResponseDto_CuandoRegistroEsExitoso()
    {
        // Arrange: para registrar un Capataz se requiere sesión de Comprador o Admin (#117)
        var request = new RegisterRequestDto("Juan Capataz", "capataz@test.com", "Password123!", UserRoles.Capataz);
        var ejecutorComprador = CrearEjecutorConRol(UserRoles.Comprador);

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        _userManager.CreateAsync(Arg.Any<AppUser>(), request.Password)
            .Returns(Task.FromResult(IdentityResult.Success));

        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), Arg.Any<string>())
            .Returns(Task.FromResult(IdentityResult.Success));

        _userManager.GetRolesAsync(Arg.Any<AppUser>())
            .Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Capataz }));

        _jwtProvider.GenerarToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>())
            .Returns("fake-jwt-token");

        // Act
        var result = await _authService.RegistrarAsync(request, ejecutorComprador);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Email, result.Email);
        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Contains(UserRoles.Capataz, result.Roles);
    }

    // ── Tarjeta #117: Matriz de permisos en RegistrarAsync ────────────────────

    [Fact]
    public async Task RegistrarAsync_AnonimoRegistrandoProveedor_DebeSerExitoso()
    {
        // Arrange
        var request = new RegisterRequestDto("Proveedor S.A.", "proveedor@test.com", "Password123!", UserRoles.Proveedor);

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));
        _userManager.CreateAsync(Arg.Any<AppUser>(), request.Password).Returns(Task.FromResult(IdentityResult.Success));
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), UserRoles.Proveedor).Returns(Task.FromResult(IdentityResult.Success));
        _userManager.GetRolesAsync(Arg.Any<AppUser>())
            .Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Proveedor }));
        _jwtProvider.GenerarToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>()).Returns("fake-jwt-token");

        // Act
        var result = await _authService.RegistrarAsync(request, ejecutor: null);

        // Assert
        Assert.Equal(request.Email, result.Email);
        Assert.Contains(UserRoles.Proveedor, result.Roles);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Comprador)]
    public async Task RegistrarAsync_AnonimoSolicitandoRolNoPublico_DebeLanzarForbiddenException(string rolSolicitado)
    {
        // Arrange
        var request = new RegisterRequestDto("Usuario", "usuario@test.com", "Password123!", rolSolicitado);
        var ejecutorAnonimo = new ClaimsPrincipal(new ClaimsIdentity());

        // Act
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request, ejecutorAnonimo));

        // Assert
        Assert.Equal("El registro público solo está permitido para el rol Proveedor.", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_CompradorSolicitandoComprador_DebeLanzarForbiddenException()
    {
        // Arrange
        var request = new RegisterRequestDto("Otro Comprador", "comprador2@test.com", "Password123!", UserRoles.Comprador);
        var ejecutorComprador = CrearEjecutorConRol(UserRoles.Comprador);

        // Act
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request, ejecutorComprador));

        // Assert
        Assert.Equal("El rol Comprador no tiene permisos para crear usuarios con rol Comprador.", ex.Message);
    }

    [Fact]
    public async Task RegistrarAsync_AdminRegistrandoComprador_DebeSerExitoso()
    {
        // Arrange
        var request = new RegisterRequestDto("Nuevo Comprador", "comprador-nuevo@test.com", "Password123!", UserRoles.Comprador);
        var ejecutorAdmin = CrearEjecutorConRol(UserRoles.Admin);

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));
        _userManager.CreateAsync(Arg.Any<AppUser>(), request.Password).Returns(Task.FromResult(IdentityResult.Success));
        _userManager.AddToRoleAsync(Arg.Any<AppUser>(), UserRoles.Comprador).Returns(Task.FromResult(IdentityResult.Success));
        _userManager.GetRolesAsync(Arg.Any<AppUser>())
            .Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Comprador }));
        _jwtProvider.GenerarToken(Arg.Any<AppUser>(), Arg.Any<IEnumerable<string>>()).Returns("fake-jwt-token");

        // Act
        var result = await _authService.RegistrarAsync(request, ejecutorAdmin);

        // Assert
        Assert.Equal(request.Email, result.Email);
        Assert.Contains(UserRoles.Comprador, result.Roles);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Proveedor)]
    public async Task RegistrarAsync_RolOperativoAutenticado_DebeLanzarForbiddenException(string rolEjecutor)
    {
        // Arrange: ni Capataz ni Proveedor autenticados tienen permisos para gestionar altas
        var request = new RegisterRequestDto("Otro Proveedor", "otro@test.com", "Password123!", UserRoles.Proveedor);
        var ejecutor = CrearEjecutorConRol(rolEjecutor);

        // Act
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _authService.RegistrarAsync(request, ejecutor));

        // Assert
        Assert.Equal("No tiene permisos para dar de alta nuevos usuarios.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarUnauthorizedAccessException_CuandoUsuarioNoExisteOEstaInactivo()
    {
        // Arrange: usuario inexistente
        var request = new LoginRequestDto("invalido@test.com", "Password123!");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        // Act
        var exNoExiste = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Arrange: usuario inactivo
        _userManager.FindByEmailAsync(request.Email)
            .Returns(Task.FromResult<AppUser?>(new AppUser { Email = request.Email, Activo = false }));

        // Act
        var exInactivo = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert: mensaje anti-enumeración exacto e idéntico (#168)
        Assert.Equal(MensajeCredencialesInvalidas, exNoExiste.Message);
        Assert.Equal(MensajeCredencialesInvalidas, exInactivo.Message);
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarUnauthorizedAccessException_CuandoPasswordEsIncorrecta()
    {
        // Arrange
        var request = new LoginRequestDto("usuario@test.com", "WrongPassword");
        var user = new AppUser { Email = request.Email, Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(false));
        _userManager.CheckPasswordAsync(user, request.Password).Returns(Task.FromResult(false));
        _userManager.AccessFailedAsync(user).Returns(Task.FromResult(IdentityResult.Success));

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert: mismo mensaje que cuando el usuario no existe y registra el intento fallido (#168)
        Assert.Equal(MensajeCredencialesInvalidas, ex.Message);
        await _userManager.Received(1).AccessFailedAsync(user);
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarUnauthorizedAccessException_CuandoCuentaYaEstaBloqueada()
    {
        // Arrange: cuenta bloqueada; incluso con la contraseña correcta debe fallar sin verificarla (#168)
        var request = new LoginRequestDto("usuario@test.com", "Password123!");
        var user = new AppUser { Email = request.Email, Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(true));

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert
        Assert.Equal(MensajeCuentaBloqueada, ex.Message);
        await _userManager.DidNotReceive().CheckPasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>());
    }

    [Fact]
    public async Task LoginAsync_DeberiaLanzarMensajeDeBloqueo_CuandoIntentoFallidoAlcanzaLimiteDeLockout()
    {
        // Arrange: antes del intento no está bloqueada (false), pero tras AccessFailedAsync pasa a bloqueada (true)
        var request = new LoginRequestDto("usuario@test.com", "WrongPassword");
        var user = new AppUser { Email = request.Email, Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(false), Task.FromResult(true));
        _userManager.CheckPasswordAsync(user, request.Password).Returns(Task.FromResult(false));
        _userManager.AccessFailedAsync(user).Returns(Task.FromResult(IdentityResult.Success));

        // Act
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.LoginAsync(request));

        // Assert
        Assert.Equal(MensajeCuentaBloqueada, ex.Message);
        await _userManager.Received(1).AccessFailedAsync(user);
    }

    [Fact]
    public async Task LoginAsync_DeberiaRetornarAuthResponseDto_CuandoCredencialesSonCorrectas()
    {
        // Arrange
        var request = new LoginRequestDto("usuario@test.com", "Password123!");
        var user = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Juan Usuario", Activo = true };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(user));
        _userManager.IsLockedOutAsync(user).Returns(Task.FromResult(false));
        _userManager.CheckPasswordAsync(user, request.Password).Returns(Task.FromResult(true));
        _userManager.ResetAccessFailedCountAsync(user).Returns(Task.FromResult(IdentityResult.Success));
        _userManager.GetRolesAsync(user).Returns(Task.FromResult<IList<string>>(new List<string> { UserRoles.Admin }));
        _jwtProvider.GenerarToken(user, Arg.Any<IEnumerable<string>>()).Returns("fake-jwt-token");

        // Act
        var result = await _authService.LoginAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(request.Email, result.Email);
        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Contains(UserRoles.Admin, result.Roles);
        await _userManager.Received(1).ResetAccessFailedCountAsync(user);
    }

    // ── Tarjeta #168: Política de contraseñas (8 caracteres y dígito) en alta ──

    [Theory]
    [InlineData("PasswordTooShort", MensajePasswordCorta)]
    [InlineData("PasswordRequiresDigit", MensajePasswordSinDigito)]
    public async Task RegistrarAsync_DebeLanzarValidacionException_CuandoPasswordIncumplePolitica(string codigoError, string mensajeEsperado)
    {
        // Arrange
        var request = new RegisterRequestDto("Proveedor S.A.", "proveedor@test.com", "ClaveInvalida", UserRoles.Proveedor);
        var errorPassword = new IdentityError { Code = codigoError, Description = "Password policy error." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));
        _userManager.CreateAsync(Arg.Any<AppUser>(), request.Password)
            .Returns(Task.FromResult(IdentityResult.Failed(errorPassword)));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RegistrarAsync(request));

        // Assert
        Assert.Equal(mensajeEsperado, ex.Message);
    }
}
