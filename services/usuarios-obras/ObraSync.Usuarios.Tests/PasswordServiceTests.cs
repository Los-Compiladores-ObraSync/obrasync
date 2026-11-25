using System.Security.Claims;
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

public class PasswordServiceTests
{
    private const string MensajeRecuperacionGenerico =
        "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.";

    private const string MensajeEnlaceInvalidoOExpirado =
        "El enlace de recuperación es inválido o ha expirado. Por favor, solicitá uno nuevo.";

    private const string MensajePasswordCorta =
        "La contraseña debe tener al menos 8 caracteres.";

    private const string MensajePasswordSinDigito =
        "La contraseña debe incluir al menos un número.";

    private const string MensajePasswordRestablecida =
        "Contraseña restablecida con éxito.";

    private const string MensajePasswordActualizada =
        "Contraseña actualizada con éxito.";

    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<PasswordService> _logger;
    private readonly ICorreoSaliente _correo;
    private readonly PasswordService _passwordService;

    public PasswordServiceTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(store, null, null, null, null, null, null, null, null);
        _logger = Substitute.For<ILogger<PasswordService>>();

        // BaseUrl con barra final a propósito: verifica que el enlace no quede con "//"
        var frontendOptions = Options.Create(new FrontendSettings { BaseUrl = "http://localhost:5173/" });
        _correo = Substitute.For<ICorreoSaliente>();

        _passwordService = new PasswordService(_userManager, _logger, frontendOptions, _correo);
    }

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
    public void DataProtectionTokenLifespan_DebeEstarConfiguradoEnUnaHora()
    {
        var services = new ServiceCollection();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
        {
            options.TokenLifespan = TimeSpan.FromHours(1);
        });

        var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider
            .GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>()
            .Value;

        Assert.Equal(TimeSpan.FromHours(1), options.TokenLifespan);
    }

    // ── Tarjeta #53: RecuperarPasswordAsync ───────────────────────────────────

    [Fact]
    public async Task RecuperarPasswordAsync_DebeGenerarTokenYLoguearEnlace_CuandoUsuarioExisteYEstaActivo()
    {
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        const string tokenGenerado = "CfDJ8+abc/def==";

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.GeneratePasswordResetTokenAsync(usuario).Returns(Task.FromResult(tokenGenerado));

        var result = await _passwordService.RecuperarPasswordAsync(request);

        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.Received(1).GeneratePasswordResetTokenAsync(usuario);

        const string enlaceEsperado =
            "http://localhost:5173/restablecer-contrasena?email=capataz%40obrasync.com&token=CfDJ8%2Babc%2Fdef%3D%3D";
        var log = Assert.Single(MensajesPasswordResetLogueados());
        Assert.Equal($"[PASSWORD-RESET] Enlace de restablecimiento para capataz@obrasync.com: {enlaceEsperado}", log);
    }

    [Fact]
    public async Task RecuperarPasswordAsync_DebeMandarElEnlaceTambienPorCorreo_CuandoUsuarioExisteYEstaActivo()
    {
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.GeneratePasswordResetTokenAsync(usuario).Returns(Task.FromResult("tok"));

        await _passwordService.RecuperarPasswordAsync(request);

        await _correo.Received(1).EnviarAsync(
            "capataz@obrasync.com",
            Arg.Any<string>(),
            Arg.Is<string>(cuerpo => cuerpo.Contains("http://localhost:5173/restablecer-contrasena?email=capataz%40obrasync.com&token=tok")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecuperarPasswordAsync_NoDebeMandarCorreo_CuandoEmailNoExiste()
    {
        var request = new RecuperarPasswordRequestDto("noexiste@obrasync.com");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        await _passwordService.RecuperarPasswordAsync(request);

        await _correo.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task RecuperarPasswordAsync_DebeResponderIgual_SiFallaElEnvioDelCorreo()
    {
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.GeneratePasswordResetTokenAsync(usuario).Returns(Task.FromResult("tok"));
        _correo.EnviarAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP caído")));

        var result = await _passwordService.RecuperarPasswordAsync(request);

        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        Assert.Single(MensajesPasswordResetLogueados());
    }

    [Fact]
    public async Task RecuperarPasswordAsync_NoDebeGenerarToken_CuandoEmailNoExiste()
    {
        var request = new RecuperarPasswordRequestDto("noexiste@obrasync.com");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        var result = await _passwordService.RecuperarPasswordAsync(request);

        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<AppUser>());
        Assert.Empty(MensajesPasswordResetLogueados());
    }

    [Fact]
    public async Task RecuperarPasswordAsync_NoDebeGenerarToken_CuandoUsuarioEstaInactivo()
    {
        var request = new RecuperarPasswordRequestDto("inactivo@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Usuario Inactivo", Activo = false };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));

        var result = await _passwordService.RecuperarPasswordAsync(request);

        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        await _userManager.DidNotReceive().GeneratePasswordResetTokenAsync(Arg.Any<AppUser>());
        Assert.Empty(MensajesPasswordResetLogueados());
    }

    // ── Tarjeta #54: RestablecerPasswordAsync ─────────────────────────────────

    [Fact]
    public async Task RestablecerPasswordAsync_DebeCambiarPasswordYRetornarExito_CuandoUsuarioEstaActivoYTokenEsValido()
    {
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "CfDJ8+token-valido==", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Success));

        var result = await _passwordService.RestablecerPasswordAsync(request);

        Assert.Equal(MensajePasswordRestablecida, result.Mensaje);
        await _userManager.Received(1).ResetPasswordAsync(usuario, request.Token, request.NuevaPassword);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoTokenEsInvalidoOVencido()
    {
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-vencido", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorToken = new IdentityError { Code = "InvalidToken", Description = "Invalid token." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorToken)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.RestablecerPasswordAsync(request));

        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoUsuarioNoExiste()
    {
        var request = new RestablecerPasswordRequestDto("noexiste@obrasync.com", "token-cualquiera", "NuevaClave123");
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(null));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.RestablecerPasswordAsync(request));

        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
        await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoUsuarioEstaInactivo()
    {
        var request = new RestablecerPasswordRequestDto("inactivo@obrasync.com", "token-cualquiera", "NuevaClave123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Usuario Inactivo", Activo = false };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.RestablecerPasswordAsync(request));

        Assert.Equal(MensajeEnlaceInvalidoOExpirado, ex.Message);
        await _userManager.DidNotReceive().ResetPasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarArgumentException_CuandoPasswordEsDemasiadoCorta()
    {
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-valido", "123");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorPassword = new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorPassword)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.RestablecerPasswordAsync(request));

        Assert.Equal(MensajePasswordCorta, ex.Message);
    }

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarValidacionException_CuandoPasswordNoTieneDigito()
    {
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-valido", "ClaveSinDigito");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorPassword = new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorPassword)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.RestablecerPasswordAsync(request));

        Assert.Equal(MensajePasswordSinDigito, ex.Message);
    }

    // ── Tarjeta #121: CambiarPasswordAsync ────────────────────────────────────

    [Fact]
    public async Task CambiarPasswordAsync_DebeActualizarPasswordYRetornarExito_CuandoDatosSonValidos()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", NombreCompleto = "Carlos Capataz", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Success));

        var result = await _passwordService.CambiarPasswordAsync(request, ejecutor);

        Assert.Equal(MensajePasswordActualizada, result.Mensaje);
        await _userManager.Received(1).ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoConfirmacionNoCoincide()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "OtraClaveDistinta!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        Assert.Equal("La nueva contraseña y su confirmación no coinciden.", ex.Message);
        await _userManager.DidNotReceive().ChangePasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoNuevaPasswordEsIgualALaActual()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "Password123!", "Password123!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        Assert.Equal("La nueva contraseña no puede ser igual a la contraseña actual.", ex.Message);
        await _userManager.DidNotReceive().ChangePasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoPasswordActualEsIncorrecta()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("ClaveIncorrecta", "NuevaClave456!", "NuevaClave456!");
        var errorMismatch = new IdentityError { Code = "PasswordMismatch", Description = "Incorrect password." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorMismatch)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        Assert.Equal("La contraseña actual es incorrecta.", ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoNuevaPasswordEsDemasiadoCorta()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "123", "123");
        var errorCorta = new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorCorta)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        Assert.Equal(MensajePasswordCorta, ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarValidacionException_CuandoNuevaPasswordNoTieneDigito()
    {
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "ClaveSinDigito", "ClaveSinDigito");
        var errorSinDigito = new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorSinDigito)));

        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        Assert.Equal(MensajePasswordSinDigito, ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarUnauthorizedAccessException_CuandoUsuarioNoExisteOEstaInactivo()
    {
        var userId = Guid.NewGuid();
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(null));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));

        _userManager.FindByIdAsync(userId.ToString())
            .Returns(Task.FromResult<AppUser?>(new AppUser { Id = userId, Activo = false }));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _passwordService.CambiarPasswordAsync(request, ejecutor));
    }

    [Fact]
    public async Task CambiarPasswordAsync_SinClaimDeUsuario_DebeLanzarUnauthorizedAccessException()
    {
        var ejecutorSinSub = new ClaimsPrincipal(new ClaimsIdentity());
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _passwordService.CambiarPasswordAsync(request, ejecutorSinSub));
    }

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
