using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Application.Services;

public class AuthService : IAuthService
{
    // Mensajes de login y bloqueo (#168)
    private const string MensajeCredencialesInvalidas =
        "Credenciales inválidas.";
    private const string MensajeCuentaBloqueada =
        "La cuenta está bloqueada temporalmente por múltiples intentos fallidos. Intentá nuevamente más tarde.";

    private readonly UserManager<AppUser> _userManager;
    private readonly IJwtProvider _jwtProvider;

    public AuthService(
        UserManager<AppUser> userManager,
        IJwtProvider jwtProvider)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _jwtProvider = jwtProvider ?? throw new ArgumentNullException(nameof(jwtProvider));
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
            PhoneNumber = string.IsNullOrWhiteSpace(request.Telefono) ? null : request.Telefono.Trim(),
            FechaCreacion = DateTime.UtcNow,
            Activo = true
        };

        // 4. Crear usuario en Identity (hashea la contraseña de forma segura)
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            IdentityPasswordErrorMapper.LanzarValidacionSiErrorDePassword(result);

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
}
