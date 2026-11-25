using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;
using ObraSync.Licitaciones.Application.MagicLinks.Queries.ObtenerPliegoPorMagicLink;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/licitaciones")]
public class MagicLinksController : ControllerBase
{
    private readonly IMediator _mediator;

    public MagicLinksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{licitacionId:guid}/magic-links")]
    [Authorize(Roles = "Comprador,Admin")] // Restringido según requerimiento #86 / #198
    [ProducesResponseType(typeof(GenerarMagicLinkResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GenerarMagicLink(
        Guid licitacionId,
        [FromBody] GenerarMagicLinkRequestDto? dto,
        CancellationToken cancellationToken)
    {
        if (dto == null || dto.ProveedorId == Guid.Empty)
        {
            return Problem(
                detail: "El identificador del proveedor es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de enlace inválidos");
        }

        try
        {
            var command = new GenerarMagicLinkCommand(licitacionId, dto.ProveedorId, dto.ProveedorNombre);
            var resultado = await _mediator.Send(command, cancellationToken);
            return Ok(resultado);
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
                title: "Licitación no disponible para generar enlace");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de enlace inválidos");
        }
    }

    /// <summary>
    /// Ver el pliego al que da acceso un Magic Link, sin consumirlo (#158). Es público: el proveedor entra sin usuario.
    /// </summary>
    [HttpGet("magic-links/{hash}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PliegoMagicLinkResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ObtenerPliegoConMagicLink(string hash, CancellationToken cancellationToken)
    {
        try
        {
            var pliego = await _mediator.Send(new ObtenerPliegoPorMagicLinkQuery(hash), cancellationToken);
            return Ok(pliego);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Enlace o licitación no encontrada");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Enlace en conflicto");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Enlace no disponible");
        }
    }

    /// <summary>
    /// Registrar una cotización mediante un Magic Link válido (acceso público sin JWT, #87 / #186).
    /// POST /api/licitaciones/magic-links/{hash}/cotizaciones
    /// </summary>
    [HttpPost("magic-links/{hash}/cotizaciones")]
    [AllowAnonymous] // Permite acceso al proveedor sin token JWT (#87 / #186)
    [ProducesResponseType(typeof(ObraSync.Licitaciones.Application.DTOs.CotizacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CotizarConMagicLink(
        string hash,
        [FromBody] CotizarConMagicLinkRequestDto? dto,
        CancellationToken cancellationToken)
    {
        if (dto == null)
        {
            return Problem(
                detail: "El cuerpo de la cotización es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de cotización inválidos");
        }

        try
        {
            var command = new CotizarConMagicLinkCommand(
                hash,
                dto.Items ?? new List<CotizacionItemDto>(),
                dto.Flete,
                dto.PlazoEntregaDias);

            var creada = await _mediator.Send(command, cancellationToken);
            return Ok(creada);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Enlace o licitación no encontrada");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Enlace o cotización en conflicto");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de cotización inválidos");
        }
    }
}

public record GenerarMagicLinkRequestDto(Guid ProveedorId, string? ProveedorNombre = null);

public record CotizarConMagicLinkRequestDto(
    List<CotizacionItemDto>? Items,
    decimal Flete,
    int PlazoEntregaDias
);
