using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// Crea un nuevo usuario en el sistema según la matriz de permisos por rol (tarjeta #117):
    /// sin token solo permite Proveedor; Comprador crea Capataz/Proveedor; Admin crea Capataz/Comprador/Proveedor.
    /// POST /api/usuarios
    /// </summary>
    [HttpPost]
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
    /// Autentica al usuario y emite el token JWT firmado.
    /// POST /api/usuarios/tokens
    /// </summary>
    [HttpPost("tokens")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var response = await _authService.LoginAsync(request);
        return Ok(response);
    }
}
