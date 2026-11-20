using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Application.Services;

public class AuthService : IAuthService
{
    // Respuesta única de "olvidé mi contraseña": es la misma exista o no el email (tarjeta #53)
    private const string MensajeRecuperacionGenerico =
        "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.";

    // Mensajes de login, bloqueo, restablecimiento y cambio de contraseña (tarjetas #54, #121 y #168)
    private const string MensajeCredencialesInvalidas =
        "Credenciales inválidas.";
    private const string MensajeCuentaBloqueada =
        "La cuenta está bloqueada temporalmente por múltiples intentos fallidos. Intentá nuevamente más tarde.";
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
    private const string MensajeConfirmacionNoCoincide =
        "La nueva contraseña y su confirmación no coinciden.";
    private const string MensajePasswordIgualAActual =
        "La nueva contraseña no puede ser igual a la contraseña actual.";
    private const string MensajePasswordActualIncorrecta =
        "La contraseña actual es incorrecta.";

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

    public async Task<AuthResponseDto> RegistrarAsync(RegisterRequestDto request, ClaimsPrincipal? ejecutor = null)
    {
        // 1. Validar que el rol recibido sea uno de los permitidos por la arquitectura
        var rolesValidos = new[] { UserRoles.Capataz, UserRoles.Comprador, UserRoles.Proveedor, UserRoles.Admin };
        if (!rolesValidos.Contains(request.Rol))
        {
            throw new ValidacionException($"El rol '{request.Rol}' no es válido. Roles permitidos: {string.Join(", ", rolesValidos)}.");
        }

        // 1b. Matriz de permisos para el alta de usuarios (tarjeta #117):
        //     - Nadie puede crear cuentas con rol Admin por este endpoint (solo por seed).
        //     - Sin token (anónimo): solo puede registrar el rol Proveedor.
        //     - Con token de Comprador: puede crear Capataz y Proveedor.
        //     - Con token de Admin: puede crear Capataz, Comprador y Proveedor.
        //     - Con token de otro rol (Capataz o Proveedor): no tiene permisos para gestionar altas.
        if (request.Rol.Equals(UserRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("No está permitido registrar cuentas con rol Admin por este medio.");
        }

        var estaAutenticado = ejecutor?.Identity?.IsAuthenticated == true;
        if (!estaAutenticado)
        {
            if (!string.Equals(request.Rol, UserRoles.Proveedor, StringComparison.Ordinal))
            {
                throw new ForbiddenException("El registro público solo está permitido para el rol Proveedor.");
            }
        }
        else if (ejecutor!.IsInRole(UserRoles.Admin))
        {
            // Admin puede crear Capataz, Comprador y Proveedor (Admin ya fue bloqueado arriba).
        }
        else if (ejecutor.IsInRole(UserRoles.Comprador))
        {
            if (string.Equals(request.Rol, UserRoles.Comprador, StringComparison.Ordinal))
            {
                throw new ForbiddenException("El rol Comprador no tiene permisos para crear usuarios con rol Comprador.");
            }
        }
        else
        {
            throw new ForbiddenException("No tiene permisos para dar de alta nuevos usuarios.");
        }

        // 2. Verificar que no exista un usuario previo con el mismo email
        var existeUsuario = await _userManager.FindByEmailAsync(request.Email);
        if (existeUsuario != null)
        {
            throw new ConflictoException("Ya existe un usuario registrado con este correo electrónico.");
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
            LanzarValidacionSiErrorDePassword(result);

            var errores = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new ConflictoException($"Error al crear el usuario: {errores}");
        }

        // 5. Asignar rol (con manejo de error y rollback si falla la asignación)
        var roleResult = await _userManager.AddToRoleAsync(user, request.Rol);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            var erroresRol = string.Join(", ", roleResult.Errors.Select(e => e.Description));
            throw new ConflictoException($"Error al asignar el rol '{request.Rol}': {erroresRol}");
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
        // 1. Buscar usuario por email. Mismo mensaje si no existe, está inactivo o la clave falla (anti-enumeración, #168)
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null || !user.Activo)
        {
            throw new UnauthorizedAccessException(MensajeCredencialesInvalidas);
        }

        // 2. Verificar si la cuenta ya se encuentra bloqueada temporalmente por intentos fallidos (#168)
        if (await _userManager.IsLockedOutAsync(user))
        {
            throw new UnauthorizedAccessException(MensajeCuentaBloqueada);
        }

        // 3. Validar contraseña y contabilizar intentos fallidos
        var passwordValida = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValida)
        {
            await _userManager.AccessFailedAsync(user);
            if (await _userManager.IsLockedOutAsync(user))
            {
                throw new UnauthorizedAccessException(MensajeCuentaBloqueada);
            }

            throw new UnauthorizedAccessException(MensajeCredencialesInvalidas);
        }

        // 4. Login exitoso: reiniciar el contador de intentos fallidos a cero
        await _userManager.ResetAccessFailedCountAsync(user);

        // 5. Obtener roles y generar JWT
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
        var (items, _) = await GetCapatacesActivosPaginadosAsync(new ParametrosPagina());
        return items;
    }

    public async Task<(IReadOnlyList<CapatazDto> Items, int Total)> GetCapatacesActivosPaginadosAsync(
        ParametrosPagina? paginacion = null)
    {
        var p = paginacion ?? new ParametrosPagina();
        var usuariosCapataz = await _userManager.GetUsersInRoleAsync(UserRoles.Capataz);

        var activos = usuariosCapataz
            .Where(u => u.Activo)
            .ToList();

        var total = activos.Count;

        var items = activos
            .OrderBy(u => u.NombreCompleto)
            .ThenBy(u => u.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .Select(u => new CapatazDto(u.Id, u.NombreCompleto))
            .ToList();

        return (items, total);
    }

    public async Task<(IReadOnlyList<ProveedorDto> Items, int Total)> GetProveedoresActivosPaginadosAsync(
        ParametrosPagina? paginacion = null)
    {
        var p = paginacion ?? new ParametrosPagina();
        var usuariosProveedor = await _userManager.GetUsersInRoleAsync(UserRoles.Proveedor);

        var activos = usuariosProveedor
            .Where(u => u.Activo)
            .ToList();

        var total = activos.Count;

        // Mismo orden que los capataces: por nombre y, si empatan, por Id (así la paginación es estable).
        var items = activos
            .OrderBy(u => u.NombreCompleto)
            .ThenBy(u => u.Id)
            .Skip(p.Saltear)
            .Take(p.Tamano)
            .Select(u => new ProveedorDto(u.Id, u.NombreCompleto))
            .ToList();

        return (items, total);
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

    public async Task<MensajeResponseDto> RestablecerPasswordAsync(RestablecerPasswordRequestDto request)
    {
        // 1. Buscar usuario por email. Si no existe o está inactivo, devolvemos el mismo 400
        //    que ante un token inválido para no revelar qué cuentas existen.
        var usuario = await _userManager.FindByEmailAsync(request.Email);
        if (usuario is null || !usuario.Activo)
        {
            throw new ValidacionException(MensajeEnlaceInvalidoOExpirado);
        }

        // 2. Aplicar la nueva contraseña verificando el token de un solo uso con Identity.
        //    Al cambiar la clave, Identity rota el SecurityStamp e invalida el token.
        var result = await _userManager.ResetPasswordAsync(usuario, request.Token, request.NuevaPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => string.Equals(e.Code, "InvalidToken", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ValidacionException(MensajeEnlaceInvalidoOExpirado);
            }

            LanzarValidacionSiErrorDePassword(result);

            var errores = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ValidacionException(
                string.IsNullOrWhiteSpace(errores) ? MensajePasswordCorta : errores);
        }

        return new MensajeResponseDto(MensajePasswordRestablecida);
    }

    public async Task<MensajeResponseDto> CambiarPasswordAsync(CambiarPasswordRequestDto request, ClaimsPrincipal ejecutor)
    {
        // 1. Extraer el ID del usuario desde el claim "sub" del JWT (con fallback a NameIdentifier)
        var userId = ejecutor?.FindFirst("sub")?.Value
                     ?? ejecutor?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("Token inválido: no se encontró el identificador del usuario.");
        }

        // 2. Verificar que el usuario exista y su cuenta esté activa
        var usuario = await _userManager.FindByIdAsync(userId);
        if (usuario is null || !usuario.Activo)
        {
            throw new UnauthorizedAccessException("Usuario no encontrado o cuenta desactivada.");
        }

        // 3. Validar coincidencia con la confirmación y que la nueva clave sea distinta de la actual
        if (!string.Equals(request.NuevaPassword, request.ConfirmarPassword, StringComparison.Ordinal))
        {
            throw new ValidacionException(MensajeConfirmacionNoCoincide);
        }

        if (string.Equals(request.PasswordActual, request.NuevaPassword, StringComparison.Ordinal))
        {
            throw new ValidacionException(MensajePasswordIgualAActual);
        }

        // 4. Cambiar contraseña mediante Identity (verifica PasswordActual y las reglas de longitud/complejidad)
        var result = await _userManager.ChangePasswordAsync(usuario, request.PasswordActual, request.NuevaPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => string.Equals(e.Code, "PasswordMismatch", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ValidacionException(MensajePasswordActualIncorrecta);
            }

            LanzarValidacionSiErrorDePassword(result);

            var errores = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ValidacionException(
                string.IsNullOrWhiteSpace(errores) ? MensajePasswordCorta : errores);
        }

        return new MensajeResponseDto(MensajePasswordActualizada);
    }

    private static void LanzarValidacionSiErrorDePassword(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code.Contains("PasswordTooShort", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidacionException(MensajePasswordCorta);
        }

        if (result.Errors.Any(e => e.Code.Contains("PasswordRequiresDigit", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidacionException(MensajePasswordSinDigito);
        }
    }
}
