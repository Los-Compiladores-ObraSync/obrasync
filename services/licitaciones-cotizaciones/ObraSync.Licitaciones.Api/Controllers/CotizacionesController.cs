using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/cotizaciones")]
[Authorize]
public class CotizacionesController : ControllerBase
{
    private readonly CrearCotizacionCommandHandler _crearCotizacionHandler;

    public CotizacionesController(CrearCotizacionCommandHandler crearCotizacionHandler)
    {
        _crearCotizacionHandler = crearCotizacionHandler;
    }

    /// <summary>
    /// Cargar una cotización para un pliego (Roles: Proveedor, Admin).
    /// Si el usuario es Proveedor, proveedorId debe coincidir exactamente con el claim 'sub' del JWT.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Proveedor,Admin")]
    [ProducesResponseType(typeof(CotizacionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CotizacionResponseDto>> CrearCotizacion(
        [FromBody] CrearCotizacionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var usuarioIdToken))
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        // Seguridad crítica: un Proveedor no puede impersonar a otro Proveedor
        if (User.IsInRole("Proveedor") && !User.IsInRole("Admin") && request.ProveedorId != usuarioIdToken)
        {
            return Problem(
                detail: "Un usuario con rol Proveedor solo puede presentar cotizaciones en su propio nombre (proveedorId debe coincidir con el claim 'sub' del token JWT).",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado por impersonación de proveedor");
        }

        try
        {
            var command = new CrearCotizacionCommand(
                request.LicitacionId,
                request.ProveedorId,
                request.Flete,
                request.PlazoEntregaDias,
                request.Items);

            var creada = await _crearCotizacionHandler.Handle(command, cancellationToken);

            return CreatedAtAction(
                nameof(LicitacionesController.ObtenerPorId),
                "Licitaciones",
                new { id = creada.LicitacionId },
                creada);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Licitación no encontrada");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Cotización duplicada");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de cotización inválidos");
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var subClaim = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
    }
}
