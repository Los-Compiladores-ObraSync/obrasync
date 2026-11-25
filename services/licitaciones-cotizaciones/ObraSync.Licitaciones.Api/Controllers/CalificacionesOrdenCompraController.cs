using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/ordenes-compra")]
[Route("api/licitaciones/ordenes-compra")]
[Authorize]
public class CalificacionesOrdenCompraController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CalificacionesOrdenCompraController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Calificar o editar la calificación de una orden de compra recibida completa (Rol: Comprador).
    /// </summary>
    [HttpPut("{id:guid}/calificacion")]
    [Authorize(Roles = "Comprador")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Orden de compra no encontrada")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "La orden de compra aún no está habilitada para calificar")]
    [MapExceptionProblem(typeof(ArgumentOutOfRangeException), StatusCodes.Status400BadRequest, "Calificación fuera de rango")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Datos de calificación inválidos")]
    [ProducesResponseType(typeof(CalificacionOrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertCalificacionOrden(
        [FromRoute] Guid id,
        [FromBody] UpsertCalificacionOrdenCompraRequestDto? request,
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

        var command = new UpsertCalificacionOrdenCompraCommand(
            OrdenCompraId: id,
            UsuarioId: usuarioId,
            Calificacion: request.Calificacion,
            Comentario: request.Comentario);

        var resultado = await _mediator.Send(command, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Consultar la calificación que el usuario autenticado cargó para una orden de compra (Roles: Comprador, Admin).
    /// </summary>
    [HttpGet("{id:guid}/calificacion")]
    [Authorize(Roles = "Comprador,Admin")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Calificación no encontrada")]
    [ProducesResponseType(typeof(CalificacionOrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMiCalificacionOrden(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var query = new GetMiCalificacionOrdenCompraQuery(id, usuarioId);
        var resultado = await _mediator.Send(query, cancellationToken);
        return Ok(resultado);
    }
}
