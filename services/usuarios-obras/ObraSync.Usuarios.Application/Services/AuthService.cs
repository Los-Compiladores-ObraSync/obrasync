using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions; // <-- NUEVO

namespace ObraSync.Usuarios.Application.Services;

public class AuthService : IAuthService
{
    // Respuesta única de "olvidé mi contraseña": es la misma exista o no el email (tarjeta #53)
    private const string MensajeRecuperacionGenerico =
        "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.";

    private readonly UserManager<AppUser> _userManager;
    private readonly IJwtProvider _jwtProvider;
    private readonly ILogger<AuthService> _logger;
    private readonly FrontendSettings _frontendSettings;

    public AuthService(
        UserManager<AppUser> userManager,
        IJwtProvider jwtProvider,
        ILogger<AuthService> logger,
        IOptions<FrontendSettings> frontendOptions)
    {
        _userManager = userManager;
        _jwtProvider = jwtProvider;
        _logger = logger;
        _frontendSettings = frontendOptions.Value;
    }

    public async Task<AuthResponseDto> RegistrarAsync(RegisterRequestDto request)
    {
        // 1. Validar que el rol recibido sea uno de los permitidos por la arquitectura
        var rolesValidos = new[] { UserRoles.Capataz, UserRoles.Comprador, UserRoles.Proveedor, UserRoles.Admin };
        if (!rolesValidos.Contains(request.Rol))
        {
            throw new ArgumentException($"El rol '{request.Rol}' no es válido. Roles permitidos: {string.Join(", ", rolesValidos)}.");
        }

        // 1b. Regla de seguridad: Restringir la auto-asignación pública del rol Admin
        if (request.Rol.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            // <-- CAMBIO: Lanzamos ForbiddenException para que el middleware devuelva 403 Forbidden
            throw new ForbiddenException("No está permitido registrar cuentas con rol Admin de forma pública.");
        }

        // 2. Verificar que no exista un usuario previo con el mismo email
        var existeUsuario = await _userManager.FindByEmailAsync(request.Email);
        if (existeUsuario != null)
        {
            throw new InvalidOperationException("Ya existe un usuario registrado con este correo electrónico.");
        }

        // 3. Crear entidad AppUser utilizando Guid como PK
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            NombreCompleto = request.NombreCompleto,
            FechaCreacion = DateTime.UtcNow,
            Activo = true
        };

        // 4. Crear usuario en Identity (hashea la contraseña de forma segura)
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errores = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Error al crear el usuario: {errores}");
        }

        // 5. Asignar rol (con manejo de error y rollback si falla la asignación)
        var roleResult = await _userManager.AddToRoleAsync(user, request.Rol);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            var erroresRol = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Error al asignar el rol '{request.Rol}': {erroresRol}");
        }

        // 6. Generar JWT y retornar AuthResponseDto
        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtProvider.GenerarToken(user, roles);

        return new AuthResponseDto(
            user.Id,
            user.NombreCompleto,
            user.Email!,
            token,
            roles
        );
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request)
    {
        // 1. Buscar usuario por email
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.Activo)
        {
            throw new UnauthorizedAccessException("Credenciales inválidas o cuenta desactivada.");
        }

        // 2. Validar contraseña
        var passwordValida = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValida)
        {
            throw new UnauthorizedAccessException("Credenciales inválidas.");
        }

        // 3. Obtener roles y generar JWT
        var roles = await _userManager.GetRolesAsync(user);
        var token = _jwtProvider.GenerarToken(user, roles);

        return new AuthResponseDto(
            user.Id,
            user.NombreCompleto,
            user.Email!,
            token,
            roles
        );
    }

    public async Task<IEnumerable<CapatazDto>> GetCapatacesActivosAsync()
    {
        var usuariosCapataz = await _userManager.GetUsersInRoleAsync(UserRoles.Capataz);

        return usuariosCapataz
            .Where(u => u.Activo)
            .Select(u => new CapatazDto(u.Id, u.NombreCompleto));
    }

    public async Task<MensajeResponseDto> RecuperarPasswordAsync(RecuperarPasswordRequestDto request)
    {
        var respuesta = new MensajeResponseDto(MensajeRecuperacionGenerico);

        // 1. Si el usuario no existe o está inactivo, no se genera token ni se loguea nada,
        //    pero se responde lo mismo para no revelar qué cuentas existen
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null || !usuario.Activo)
        {
            return respuesta;
        }

        // 2. Token de Identity: queda atado al SecurityStamp del usuario, así que deja de servir
        //    cuando se cambia la contraseña (un solo uso). Vence según TokenLifespan (1 hora, en Program.cs)
        var token = await _userManager.GeneratePasswordResetTokenAsync(usuario);

        // 3. Enlace a la pantalla del frontend, con email y token codificados para ir en la URL
        var baseUrl = string.IsNullOrWhiteSpace(_frontendSettings.BaseUrl)
            ? FrontendSettings.BaseUrlPorDefecto
            : _frontendSettings.BaseUrl;
        var escapedEmail = Uri.EscapeDataString(usuario.Email!);
        var escapedToken = Uri.EscapeDataString(token);
        var enlace = $"{baseUrl.TrimEnd('/')}/restablecer-contrasena?email={escapedEmail}&token={escapedToken}";

        // 4. Entrega por log (decisión #51: desarrollo y demo, sin SMTP). Plantilla estructurada, sin $"..." (CA2254)
        _logger.LogInformation(
            "[PASSWORD-RESET] Enlace de restablecimiento para {Email}: {Enlace}",
            usuario.Email,
            enlace);

        return respuesta;
    }
}