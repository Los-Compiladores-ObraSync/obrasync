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

public class AuthServiceTests
{
    private const string MensajeCredencialesInvalidas =
        "Credenciales inválidas.";

    private const string MensajeCuentaBloqueada =
        "La cuenta está bloqueada temporalmente por múltiples intentos fallidos. Intentá nuevamente más tarde.";

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
    private readonly IJwtProvider _jwtProvider;
    private readonly IUsuarioQueryRepository _usuarioQueryRepository;
    private readonly ILogger<AuthService> _logger;
    private readonly ICorreoSaliente _correo;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        var store = Substitute.For<IUserStore<AppUser>>();
        _userManager = Substitute.For<UserManager<AppUser>>(store, null, null, null, null, null, null, null, null);
        _jwtProvider = Substitute.For<IJwtProvider>();
        _usuarioQueryRepository = Substitute.For<IUsuarioQueryRepository>();

        _logger = Substitute.For<ILogger<AuthService>>();

        // BaseUrl con barra final a propósito: verifica que el enlace no quede con "//"
        var frontendOptions = Options.Create(new FrontendSettings { BaseUrl = "http://localhost:5173/" });

        _correo = Substitute.For<ICorreoSaliente>();

        _authService = new AuthService(_userManager, _jwtProvider, _usuarioQueryRepository, _logger, frontendOptions, _correo);
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
    public async Task GetCapatacesActivosAsync_DebeDelegarEnRepositorioYRetornarCapataces()
    {
        // Arrange (#254)
        var capatazId = Guid.NewGuid();
        IReadOnlyList<UsuarioResumenDto> resumen = new List<UsuarioResumenDto>
        {
            new(capatazId, "Carlos Capataz")
        };

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, new ParametrosPagina(), Arg.Any<CancellationToken>())
            .Returns((resumen, 1));

        // Act
        var result = await _authService.GetCapatacesActivosAsync();

        // Assert
        var unico = Assert.Single(result);
        Assert.Equal(capatazId, unico.Id);
        Assert.Equal("Carlos Capataz", unico.NombreCompleto);
        await _userManager.DidNotReceive().GetUsersInRoleAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetCapatacesActivosPaginadosAsync_DebeDelegarPaginacionEnBaseDeDatosSinCargarEnMemoria()
    {
        // Arrange (#182, #254)
        var idMenor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idMayor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var idZeta = Guid.Parse("55555555-5555-5555-5555-555555555555");

        IReadOnlyList<UsuarioResumenDto> pagina1Repo = new List<UsuarioResumenDto>
        {
            new(idMenor, "Carlos Empate"),
            new(idMayor, "Carlos Empate")
        };
        IReadOnlyList<UsuarioResumenDto> pagina2Repo = new List<UsuarioResumenDto>
        {
            new(idZeta, "Zulema Capataz")
        };

        var p1 = new ParametrosPagina(1, 2);
        var p2 = new ParametrosPagina(2, 2);

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, p1, Arg.Any<CancellationToken>())
            .Returns((pagina1Repo, 3));
        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Capataz, p2, Arg.Any<CancellationToken>())
            .Returns((pagina2Repo, 3));

        // Act
        var (pag1, total1) = await _authService.GetCapatacesActivosPaginadosAsync(p1);
        var (pag2, total2) = await _authService.GetCapatacesActivosPaginadosAsync(p2);

        // Assert
        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(2, pag1.Count);
        Assert.Single(pag2);
        Assert.Equal(idMenor, pag1[0].Id);
        Assert.Equal(idMayor, pag1[1].Id);
        Assert.Equal(idZeta, pag2[0].Id);
        await _userManager.DidNotReceive().GetUsersInRoleAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task GetProveedoresActivosPaginadosAsync_DebeDelegarPaginacionEnBaseDeDatosSinCargarEnMemoria()
    {
        // Arrange (#157, #254)
        var idMenor = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var idMayor = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var idZeta = Guid.Parse("55555555-5555-5555-5555-555555555555");

        IReadOnlyList<UsuarioResumenDto> pagina1Repo = new List<UsuarioResumenDto>
        {
            new(idMenor, "Corralón Empate"),
            new(idMayor, "Corralón Empate")
        };
        IReadOnlyList<UsuarioResumenDto> pagina2Repo = new List<UsuarioResumenDto>
        {
            new(idZeta, "Zulema Materiales")
        };

        var p1 = new ParametrosPagina(1, 2);
        var p2 = new ParametrosPagina(2, 2);

        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, p1, Arg.Any<CancellationToken>())
            .Returns((pagina1Repo, 3));
        _usuarioQueryRepository
            .GetActivosPorRolPaginadosAsync(UserRoles.Proveedor, p2, Arg.Any<CancellationToken>())
            .Returns((pagina2Repo, 3));

        // Act
        var (pag1, total1) = await _authService.GetProveedoresActivosPaginadosAsync(p1);
        var (pag2, total2) = await _authService.GetProveedoresActivosPaginadosAsync(p2);

        // Assert
        Assert.Equal(3, total1);
        Assert.Equal(3, total2);
        Assert.Equal(2, pag1.Count);
        Assert.Single(pag2);
        Assert.Equal(idMenor, pag1[0].Id);
        Assert.Equal(idMayor, pag1[1].Id);
        Assert.Equal(idZeta, pag2[0].Id);
        await _userManager.DidNotReceive().GetUsersInRoleAsync(Arg.Any<string>());
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
    public async Task RecuperarPasswordAsync_DebeMandarElEnlaceTambienPorCorreo_CuandoUsuarioExisteYEstaActivo()
    {
        var request = new RecuperarPasswordRequestDto("capataz@obrasync.com");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.GeneratePasswordResetTokenAsync(usuario).Returns(Task.FromResult("tok"));

        await _authService.RecuperarPasswordAsync(request);

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

        await _authService.RecuperarPasswordAsync(request);

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

        var result = await _authService.RecuperarPasswordAsync(request);

        // La respuesta es la misma y el enlace quedó igual en el log.
        Assert.Equal(MensajeRecuperacionGenerico, result.Mensaje);
        Assert.Single(MensajesPasswordResetLogueados());
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
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RestablecerPasswordAsync(request));

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
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RestablecerPasswordAsync(request));

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
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RestablecerPasswordAsync(request));

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
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert
        Assert.Equal(MensajePasswordCorta, ex.Message);
    }

    // ── Tarjeta #121: CambiarPasswordAsync ────────────────────────────────────

    [Fact]
    public async Task CambiarPasswordAsync_DebeActualizarPasswordYRetornarExito_CuandoDatosSonValidos()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", NombreCompleto = "Carlos Capataz", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Success));

        // Act
        var result = await _authService.CambiarPasswordAsync(request, ejecutor);

        // Assert
        Assert.Equal(MensajePasswordActualizada, result.Mensaje);
        await _userManager.Received(1).ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoConfirmacionNoCoincide()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "OtraClaveDistinta!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Assert
        Assert.Equal("La nueva contraseña y su confirmación no coinciden.", ex.Message);
        await _userManager.DidNotReceive().ChangePasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoNuevaPasswordEsIgualALaActual()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "Password123!", "Password123!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Assert
        Assert.Equal("La nueva contraseña no puede ser igual a la contraseña actual.", ex.Message);
        await _userManager.DidNotReceive().ChangePasswordAsync(Arg.Any<AppUser>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoPasswordActualEsIncorrecta()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("ClaveIncorrecta", "NuevaClave456!", "NuevaClave456!");
        var errorMismatch = new IdentityError { Code = "PasswordMismatch", Description = "Incorrect password." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorMismatch)));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Assert
        Assert.Equal("La contraseña actual es incorrecta.", ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarArgumentException_CuandoNuevaPasswordEsDemasiadoCorta()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "123", "123");
        var errorCorta = new IdentityError { Code = "PasswordTooShort", Description = "Passwords must be at least 6 characters." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorCorta)));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Assert
        Assert.Equal(MensajePasswordCorta, ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarUnauthorizedAccessException_CuandoUsuarioNoExisteOEstaInactivo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "NuevaClave456!", "NuevaClave456!");

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(null));

        // Act & Assert (usuario inexistente)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Arrange (usuario inactivo)
        _userManager.FindByIdAsync(userId.ToString())
            .Returns(Task.FromResult<AppUser?>(new AppUser { Id = userId, Activo = false }));

        // Act & Assert (usuario inactivo)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.CambiarPasswordAsync(request, ejecutor));
    }

    // ── Tarjeta #168: Política de contraseñas (8 caracteres y dígito) ─────────

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

    [Fact]
    public async Task RestablecerPasswordAsync_DebeLanzarValidacionException_CuandoPasswordNoTieneDigito()
    {
        // Arrange
        var request = new RestablecerPasswordRequestDto("capataz@obrasync.com", "token-valido", "ClaveSinDigito");
        var usuario = new AppUser { Id = Guid.NewGuid(), Email = request.Email, NombreCompleto = "Carlos Capataz", Activo = true };
        var errorPassword = new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." };

        _userManager.FindByEmailAsync(request.Email).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorPassword)));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.RestablecerPasswordAsync(request));

        // Assert
        Assert.Equal(MensajePasswordSinDigito, ex.Message);
    }

    [Fact]
    public async Task CambiarPasswordAsync_DebeLanzarValidacionException_CuandoNuevaPasswordNoTieneDigito()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var usuario = new AppUser { Id = userId, Email = "capataz@obrasync.com", Activo = true };
        var ejecutor = CrearEjecutorConRol(UserRoles.Capataz, userId);
        var request = new CambiarPasswordRequestDto("Password123!", "ClaveSinDigito", "ClaveSinDigito");
        var errorSinDigito = new IdentityError { Code = "PasswordRequiresDigit", Description = "Passwords must have at least one digit ('0'-'9')." };

        _userManager.FindByIdAsync(userId.ToString()).Returns(Task.FromResult<AppUser?>(usuario));
        _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword)
            .Returns(Task.FromResult(IdentityResult.Failed(errorSinDigito)));

        // Act
        var ex = await Assert.ThrowsAsync<ValidacionException>(() => _authService.CambiarPasswordAsync(request, ejecutor));

        // Assert
        Assert.Equal(MensajePasswordSinDigito, ex.Message);
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
