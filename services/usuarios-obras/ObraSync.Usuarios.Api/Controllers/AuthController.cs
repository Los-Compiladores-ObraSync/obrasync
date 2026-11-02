using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Registra un nuevo usuario en el sistema según la matriz de permisos por rol (tarjeta #117):
    /// sin token solo permite Proveedor; Comprador crea Capataz/Proveedor; Admin crea Capataz/Comprador/Proveedor.
    /// </summary>
    [HttpPost("registro")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Registrar([FromBody] RegisterRequestDto request)
    {
        // Si el cliente envió cabecera Authorization pero el token es inválido o venció,
        // devolvemos 401 antes de evaluar los permisos como anónimo.
        if (Request?.Headers.ContainsKey("Authorization") == true && !(User.Identity?.IsAuthenticated ?? false))
        {
            throw new UnauthorizedAccessException("El token proporcionado es inválido o ha expirado.");
        }

        var response = await _authService.RegistrarAsync(request, User);
        return CreatedAtAction(nameof(Registrar), new { id = response.Id }, response);
    }

    /// <summary>
    /// Inicia sesión de usuario y retorna el token JWT firmado.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var response = await _authService.LoginAsync(request);
        return Ok(response);
    }

    /// <summary>
    /// Inicia la recuperación de contraseña: si el email existe y la cuenta está activa,
    /// genera un token de un solo uso (vigencia: 1 hora) y emite el enlace en los logs del servicio.
    /// Siempre responde lo mismo, exista o no el email, para no revelar qué cuentas existen.
    /// </summary>
    /// <remarks>
    /// Endpoint público (sin JWT). RIESGO CONOCIDO: no tiene límite de intentos (rate limiting);
    /// queda fuera del alcance de este sprint (tarjeta #53).
    /// </remarks>
    [HttpPost("password/recuperar")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MensajeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecuperarPassword([FromBody] RecuperarPasswordRequestDto request)
    {
        return Ok(await _authService.RecuperarPasswordAsync(request));
    }

    /// <summary>
    /// Restablece la contraseña del usuario validando el token de recuperación de un solo uso.
    /// </summary>
    [HttpPost("password/restablecer")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MensajeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RestablecerPassword([FromBody] RestablecerPasswordRequestDto request)
    {
        return Ok(await _authService.RestablecerPasswordAsync(request));
    }

    [HttpGet("capataces")]
    [Authorize(Roles = $"{UserRoles.Comprador},{UserRoles.Admin}")]
    public async Task<ActionResult<IEnumerable<CapatazDto>>> GetCapatacesActivos()
    {
        var capataces = await _authService.GetCapatacesActivosAsync();
        return Ok(capataces);
    }
}
