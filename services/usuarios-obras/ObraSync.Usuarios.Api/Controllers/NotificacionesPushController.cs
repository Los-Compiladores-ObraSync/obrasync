using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Api.Controllers;

[ApiController]
[Route("api/usuarios/notificaciones")]
[Authorize]
public class NotificacionesPushController : ControllerBase
{
    private readonly ISuscripcionPushService _suscripcionService;
    private readonly IHostEnvironment _environment;

    public NotificacionesPushController(
        ISuscripcionPushService suscripcionService,
        IHostEnvironment environment)
    {
        _suscripcionService = suscripcionService ?? throw new ArgumentNullException(nameof(suscripcionService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Obtiene la clave pública VAPID del servidor para suscribirse a Web Push (#227).
    /// </summary>
    [HttpGet("vapid-public-key")]
    [ProducesResponseType(typeof(VapidPublicKeyResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public ActionResult<VapidPublicKeyResponseDto> ObtenerClavePublicaVapid()
    {
        return Ok(_suscripcionService.ObtenerClavePublicaVapid());
    }

    /// <summary>
    /// Registra o actualiza una suscripción Web Push del usuario autenticado (máximo 5 por usuario, #227).
    /// </summary>
    [HttpPost("suscripciones")]
    [ProducesResponseType(typeof(SuscripcionPushResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SuscripcionPushResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RegistrarSuscripcion(
        [FromBody] CrearSuscripcionPushRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var usuarioId = ObtenerUsuarioIdAutenticado();
        var userAgent = Request?.Headers.UserAgent.ToString();
        var permitirHttpLocalhost = !_environment.IsProduction();

        var (suscripcion, esNueva) = await _suscripcionService.RegistrarSuscripcionAsync(
            usuarioId: usuarioId,
            request: request!,
            userAgentHeader: userAgent,
            permitirHttpLocalhost: permitirHttpLocalhost,
            cancellationToken: cancellationToken);

        return esNueva
            ? StatusCode(StatusCodes.Status201Created, suscripcion)
            : Ok(suscripcion);
    }

    /// <summary>
    /// Elimina una suscripción Web Push propia filtrando por endpoint (#227).
    /// </summary>
    [HttpDelete("suscripciones")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EliminarSuscripcionPorEndpoint(
        [FromQuery] string? endpoint,
        CancellationToken cancellationToken = default)
    {
        var usuarioId = ObtenerUsuarioIdAutenticado();
        await _suscripcionService.EliminarPorEndpointAsync(usuarioId, endpoint, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Elimina una suscripción Web Push propia por identificador (#227).
    /// </summary>
    [HttpDelete("suscripciones/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EliminarSuscripcionPorId(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var usuarioId = ObtenerUsuarioIdAutenticado();
        await _suscripcionService.EliminarPorIdAsync(usuarioId, id, cancellationToken);
        return NoContent();
    }

    private Guid ObtenerUsuarioIdAutenticado()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var usuarioId) || usuarioId == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al usuario.");
        }

        return usuarioId;
    }
}
