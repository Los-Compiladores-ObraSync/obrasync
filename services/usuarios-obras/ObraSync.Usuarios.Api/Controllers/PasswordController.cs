using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/usuarios/password")]
public class PasswordController : ControllerBase
{
    private readonly IPasswordService _passwordService;

    public PasswordController(IPasswordService passwordService)
    {
        _passwordService = passwordService ?? throw new ArgumentNullException(nameof(passwordService));
    }

    /// <summary>
    /// Crea una solicitud de recuperación de contraseña: si el email existe y la cuenta está activa,
    /// genera un token de un solo uso (vigencia: 1 hora) y emite el enlace en los logs del servicio.
    /// Siempre responde lo mismo, exista o no el email, para no revelar qué cuentas existen.
    /// POST /api/usuarios/password/recuperaciones
    /// </summary>
    /// <remarks>
    /// Endpoint público (sin JWT). RIESGO CONOCIDO: no tiene límite de intentos (rate limiting);
    /// queda fuera del alcance de este sprint (tarjeta #53).
    /// </remarks>
    [HttpPost("recuperaciones")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MensajeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecuperarPassword([FromBody] RecuperarPasswordRequestDto request)
    {
        return Ok(await _passwordService.RecuperarPasswordAsync(request));
    }

    /// <summary>
    /// Registra el restablecimiento de contraseña del usuario validando el token de recuperación de un solo uso.
    /// POST /api/usuarios/password/restablecimientos
    /// </summary>
    [HttpPost("restablecimientos")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MensajeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RestablecerPassword([FromBody] RestablecerPasswordRequestDto request)
    {
        return Ok(await _passwordService.RestablecerPasswordAsync(request));
    }

    /// <summary>
    /// Registra el cambio de contraseña del usuario autenticado verificando su contraseña actual (tarjeta #121).
    /// POST /api/usuarios/password/cambios
    /// </summary>
    [HttpPost("cambios")]
    [Authorize]
    [ProducesResponseType(typeof(MensajeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CambiarPassword([FromBody] CambiarPasswordRequestDto request)
    {
        return Ok(await _passwordService.CambiarPasswordAsync(request, User));
    }
}
