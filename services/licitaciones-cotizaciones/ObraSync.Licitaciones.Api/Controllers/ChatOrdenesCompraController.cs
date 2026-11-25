using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;
using ObraSync.Licitaciones.Application.UseCases.Chat.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/ordenes-compra")]
[Route("api/licitaciones/ordenes-compra")]
[Authorize]
public class ChatOrdenesCompraController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ChatOrdenesCompraController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Listar los mensajes del chat de una orden de compra con paginación
    /// (Roles: Comprador, Admin, Proveedor dueño de la orden).
    /// </summary>
    [HttpGet("{id:guid}/chat/mensajes")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(IReadOnlyList<MensajeChatOrdenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListarMensajesChat(
        [FromRoute] Guid id,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryCrearPaginacion(pagina, tamano, out var paginacion, out var errorResult))
        {
            return errorResult!;
        }

        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var resultado = await _mediator.Send(
            new GetMensajesChatOrdenQuery(id, usuarioId, ObtenerRolUsuario(), paginacion),
            cancellationToken);

        EscribirHeadersPaginacion(resultado.Total, paginacion);
        return Ok(resultado.Items);
    }

    /// <summary>
    /// Enviar un mensaje al chat de una orden de compra
    /// (Roles: Comprador, Proveedor dueño de la orden; Admin solo lectura).
    /// </summary>
    [HttpPost("{id:guid}/chat/mensajes")]
    [Authorize(Roles = "Comprador,Proveedor")]
    [ProducesResponseType(typeof(MensajeChatOrdenDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnviarMensajeChat(
        [FromRoute] Guid id,
        [FromBody] EnviarMensajeChatRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Problem(
                detail: "El cuerpo de la petición es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Petición inválida");
        }

        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var command = new EnviarMensajeChatOrdenCommand(
            OrdenCompraId: id,
            UsuarioId: usuarioId,
            UsuarioNombre: ObtenerNombreUsuario(),
            RolUsuario: ObtenerRolUsuario(),
            Texto: request.Texto,
            CorrelationId: ObtenerCorrelationId());

        var dto = await _mediator.Send(command, cancellationToken);
        return CreatedAtAction(nameof(ListarMensajesChat), new { id }, dto);
    }

    /// <summary>
    /// Registrar la lectura del chat de una orden de compra para el usuario autenticado
    /// (Roles: Comprador, Admin, Proveedor dueño de la orden). Operación idempotente.
    /// POST /api/ordenes-compra/{id}/chat/lecturas
    /// </summary>
    [HttpPost("{id:guid}/chat/lecturas")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(LecturaChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarcarChatLeido(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var dto = await _mediator.Send(
            new MarcarChatOrdenLeidoCommand(id, usuarioId, ObtenerRolUsuario()),
            cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// Consultar la cantidad de mensajes no leídos por orden de compra para el usuario actual
    /// (Roles: Comprador, Admin, Proveedor).
    /// </summary>
    [HttpGet("chat/no-leidos")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(ResumenNoLeidosChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ObtenerNoLeidosChat(CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var dto = await _mediator.Send(
            new GetNoLeidosChatOrdenesQuery(usuarioId, ObtenerRolUsuario()),
            cancellationToken);
        return Ok(dto);
    }
}
