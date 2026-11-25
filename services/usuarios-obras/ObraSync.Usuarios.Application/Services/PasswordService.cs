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

public class PasswordService : IPasswordService
{
    // Respuesta única de "olvidé mi contraseña": es la misma exista o no el email (tarjeta #53)
    private const string MensajeRecuperacionGenerico =
        "Si el correo está registrado, se enviaron las instrucciones para restablecer la contraseña.";

    // Mensajes de restablecimiento y cambio de contraseña (tarjetas #54, #121 y #168)
    private const string MensajeEnlaceInvalidoOExpirado =
        "El enlace de recuperación es inválido o ha expirado. Por favor, solicitá uno nuevo.";
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
    private readonly ILogger<PasswordService> _logger;
    private readonly FrontendSettings _frontendSettings;
    private readonly ICorreoSaliente _correo;

    public PasswordService(
        UserManager<AppUser> userManager,
        ILogger<PasswordService> logger,
        IOptions<FrontendSettings> frontendOptions,
        ICorreoSaliente correo)
    {
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _frontendSettings = frontendOptions?.Value ?? throw new ArgumentNullException(nameof(frontendOptions));
        _correo = correo ?? throw new ArgumentNullException(nameof(correo));
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

        // 5. Además se manda por correo (en desarrollo lo recibe Mailpit). Si el envío falla no se cambia la
        //    respuesta: sería otra forma de revelar qué cuentas existen. El enlace ya quedó en el log.
        try
        {
            await _correo.EnviarAsync(
                usuario.Email!,
                "Restablecé tu contraseña de ObraSync",
                $"Hola {usuario.NombreCompleto}:\n\n" +
                "Pediste restablecer tu contraseña. Entrá a este enlace (sirve una sola vez y vence en 1 hora):\n\n" +
                $"{enlace}\n\n" +
                "Si no fuiste vos, ignorá este mensaje: tu contraseña no cambia.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo enviar el correo de recuperación a {Email}.", usuario.Email);
        }

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

            IdentityPasswordErrorMapper.LanzarValidacionSiErrorDePassword(result);

            var errores = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ValidacionException(
                string.IsNullOrWhiteSpace(errores) ? IdentityPasswordErrorMapper.MensajePasswordCorta : errores);
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

            IdentityPasswordErrorMapper.LanzarValidacionSiErrorDePassword(result);

            var errores = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ValidacionException(
                string.IsNullOrWhiteSpace(errores) ? IdentityPasswordErrorMapper.MensajePasswordCorta : errores);
        }

        return new MensajeResponseDto(MensajePasswordActualizada);
    }
}
