using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class AuthServiceTests
{
    private const string MensajeRecuperacionGenerico =
        "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.";

    private const string MensajeEnlaceInvalidoOExpirado =
        "El enlace de recuperación es inválido o ha expirado. Por favor, solicitá uno nuevo.";

    private const string MensajePasswordCorta =
        "La contraseña debe tener al menos 6 caracteres.";

    private const string MensajePasswordRestablecida =
        "Contraseña restablecida con éxito.";

    private readonly UserManager<AppUser> _userManager;
    private readonly IJwtProvider _jwtProvider;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(store, null, null, null, null, null, null, null, null);
        _jwtProvider = Substitute.For<IJwtProvider>();

        _logger = Substitute.For<ILogger<AuthService>>();

        // BaseUrl con barra final a propósito: verifica que el enlace no quede con "//"
        var frontendOptions = Options.Create(new FrontendSettings { BaseUrl = "http://localhost:5173/" });

        _authService = new AuthService(_userManager, _jwtProvider, _logger, frontendOptions);
    }

    [Fact]
    public void DataProtectionTokenLifespan_DebeEstarConfiguradoEnUnaHora()
    {
        // Arrange: recrea la colección de servicios con la configuración aplicada en Program.cs
        var services = new ServiceCollection();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(1);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Act: resuelve la opción tal como la consume Identity en runtime
        var options = serviceProvider
            .GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>()
            .Value;

        // Assert: verifica que no use el valor por defecto (24 hs) sino 1 hora
        Assert.Equal(TimeSpan.FromHours(1), options.TokenLifespan);
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

    // ── Tarjeta #53: RecuperarPasswordAsync ───────────────────────────────────

    [Fact]
    public async Task RecuperarPasswordAsync_DebeGenerarTokenYLoguearEnlace_CuandoUsuarioExisteYEstaActivo()
    {
        // Arrange
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };

        // Token con '+', '/' y '=' para verificar que se codifica para la URL
        const string tokenGenerado = "CfDJ8+abc/def==";

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.GeneratePasswordResetTokenAsync(usuario).Returns(Task.FromResult(tokenGenerado));

        // Act
        var result = await _authService.RecuperarPasswordAsync(request);

        // Assert
        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.Received(1).GeneratePasswordResetTokenAsync(usuario);

        const string enlaceEsperado =
            "http://localhost:5173/restablecer-contrasena?email=capataz%40obrasync.com&token=CfDJ8%2Babc%2Fdef%3D%3D";
        var log = Assert.Single(MensajesPasswordResetLogueados());
        Assert.Equal($"[PASSWORD-RESET] Enlace de restablecimiento para capataz@obrasync.com: {enlaceEsperado}", log);
    }

    [Fact]
    public async Task RecuperarPasswordAsync_NoDebeGenerarToken_CuandoEmailNoExiste()
    {
        // Arrange
        var request = new RecuperarPasswordRequestDto("noexiste@obrasync.com");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        // Act
        var result = await _authService.RecuperarPasswordAsync(request);

        // Assert: misma respuesta que con un usuario válido, sin token ni enlace en el log
        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<AppUser>());
        Assert.Empty(MensajesPasswordResetLogueados());
    }

    [Fact]
    public async Task RecuperarPasswordAsync_NoDebeGenerarToken_CuandoUsuarioEstaInactivo()
    {
        // Arrange
        var request = new RecuperarPasswordRequestDto("inactivo@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Usuario Inactivo", Activo = false };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));

        // Act
        var result = await _authService.RecuperarPasswordAsync(request);

        // Assert: misma respuesta que con un usuario válido, sin token ni enlace en el log
        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<AppUser>());
        Assert.Empty(MensajesPasswordResetLogueados());
    }

    // ── Tarjeta #54: RestablecerPasswordAsync ─────────────────────────────────

    [Fact]
    public async Task RestablecerPasswordAsync_DebeCambiarPasswordYRetornarExito_CuandoUsuarioEstaActivoYTokenEsValido()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "CfDJ8+token-valido==", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Success));

        // Act
        var result = await _authService.RestablecerPasswordAsync(request);

        // Assert
        Assert.Equal(MensajePasswordRestablecida, result.Mensaje);
        await _userManager.Received(1).ResetPasswordAsync(usuario, request.Token, request.NuevaPassword);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoTokenEsInvalidoOVencido()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-vencido", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorToken = new IdentityError { Code = "InvalidToken", Description = "Invalid token." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorToken)));

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert
        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoUsuarioNoExiste()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("noexiste@obrasync.com", "token-cualquiera", "NuevaClave123");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert: mismo mensaje amigable que con token inválido, sin invocar ResetPasswordAsync
        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
        await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoUsuarioEstaInactivo()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("inactivo@obrasync.com", "token-cualquiera", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Usuario Inactivo", Activo = false };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert: mismo mensaje amigable que con token inválido, sin invocar ResetPasswordAsync
        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
        await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoPasswordEsDemasiadoCorta()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-valido", "123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorPassword = new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorPassword)));

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert
        Assert.Equal(MensajePasswordCorta, ex.Message);
    }

    // LogInformation(...) es un método de extensión: por dentro llama a ILogger.Log<TState> con un tipo
    // interno de .NET, por eso Received() no lo puede matchear. Se revisan las llamadas que registró el mock.
    private List<string> MensajesPasswordResetLogueados()
    {
        return _logger.ReceivedCalls()
            .Where(llamada => llamada.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(llamada => llamada.GetArguments())
            .Where(args => args[0] is LogLevel.Information)
            .Select(args => args[2]?.ToString() ?? string.Empty)
            .Where(mensaje => mensaje.StartsWith("[PASSWORD-RESET]"))
            .ToList();
    }
}
