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
    /// Registra un nuevo usuario en el sistema con un rol asignado.
    /// </summary>
    [HttpPost("registro")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Registrar([FromBody] RegisterRequestDto request)
    {
        var response = await _authService.RegistrarAsync(request);
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

    [HttpGet("capataces")]
    [Authorize(Roles = $"{UserRoles.Comprador},{UserRoles.Admin}")]
    public async Task<ActionResult<IEnumerable<CapatazDto>>> GetCapatacesActivos()
    {
        var capataces = await _authService.GetCapatacesActivosAsync();
        return Ok(capataces);
    }
}
