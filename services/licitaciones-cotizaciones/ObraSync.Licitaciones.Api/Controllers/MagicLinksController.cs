using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

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
    [Authorize(Roles = "Comprador,Admin")] // Restringido según requerimiento #86
    public async Task<IActionResult> GenerarMagicLink(Guid licitacionId, [FromBody] GenerarMagicLinkRequestDto dto, CancellationToken cancellationToken)
    {
        var command = new GenerarMagicLinkCommand(licitacionId, dto.ProveedorId);
        var hash = await _mediator.Send(command, cancellationToken);

        return Ok(new { Hash = hash, ExpiresIn = "1 hour" });
    }

    [HttpPost("magic-links/{hash}/cotizar")]
    [AllowAnonymous] // Requerimiento clave: permite acceso al proveedor sin token JWT[cite: 3]
    public async Task<IActionResult> CotizarConMagicLink(
        string hash, 
        [FromBody] CotizarConMagicLinkRequestDto dto, 
        CancellationToken cancellationToken)
    {
        var command = new CotizarConMagicLinkCommand(hash, dto.Items, dto.Flete, dto.PlazoEntregaDias);
        await _mediator.Send(command, cancellationToken);

        return Ok(new { Mensaje = "Cotización enviada con éxito mediante Magic Link." });
    }
}

public record GenerarMagicLinkRequestDto(Guid ProveedorId);

public record CotizarConMagicLinkRequestDto(
    List<CotizacionItemDto> Items,
    decimal Flete,
    int PlazoEntregaDias
);