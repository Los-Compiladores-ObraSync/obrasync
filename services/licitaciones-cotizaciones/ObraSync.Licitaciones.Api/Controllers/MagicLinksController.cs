using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

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
}

public record GenerarMagicLinkRequestDto(Guid ProveedorId);