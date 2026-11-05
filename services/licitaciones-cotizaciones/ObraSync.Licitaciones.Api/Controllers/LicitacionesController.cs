using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Exceptions;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/licitaciones")]
[Authorize]
public class LicitacionesController : ControllerBase
{
    private readonly ILicitacionService _service;
    private readonly GetLicitacionesQueryHandler _getLicitacionesHandler;
    private readonly GetLicitacionByIdQueryHandler _getByIdHandler;
    private readonly IMediator _mediator;

    public LicitacionesController(
        ILicitacionService service,
        GetLicitacionesQueryHandler getLicitacionesHandler,
        GetLicitacionByIdQueryHandler getByIdHandler,
        IMediator mediator)
    {
        _service = service;
        _getLicitacionesHandler = getLicitacionesHandler;
        _getByIdHandler = getByIdHandler;
        _mediator = mediator;
    }

    /// <summary>
    /// Listar pliegos con filtros opcionales (Roles: Comprador, Proveedor, Admin).
    /// Un Proveedor solo visualiza pliegos en estado Abierta.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Comprador,Proveedor,Admin")]
    [ProducesResponseType(typeof(IEnumerable<LicitacionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LicitacionResponseDto>>> ListarLicitaciones(
        [FromQuery] Guid? obraId = null,
        [FromQuery] string? estado = null,
        CancellationToken cancellationToken = default)
    {
        var rolUsuario = User.IsInRole("Proveedor")
            ? "Proveedor"
            : User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);

        var query = new GetLicitacionesQuery(obraId, estado, rolUsuario);
        var licitaciones = await _getLicitacionesHandler.Handle(query, cancellationToken);
        return Ok(licitaciones);
    }

    /// <summary>
    /// Obtener un pliego por Id (Roles: Comprador, Proveedor, Admin)
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Comprador,Proveedor,Admin")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LicitacionResponseDto>> ObtenerPorId(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new GetLicitacionByIdQuery(id);
            var licitacion = await _getByIdHandler.Handle(query, cancellationToken);
            return Ok(licitacion);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Licitación no encontrada");
        }
    }

    /// <summary>
    /// Publicar una licitación a partir de una requisición aprobada (Roles: Comprador, Admin).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(
        [FromBody] CrearLicitacionRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var licitacion = await _service.CrearAsync(request, ct);
            return CreatedAtAction(
                nameof(ObtenerPorId),
                new { id = licitacion.LicitacionId },
                licitacion);
        }
        catch (ValidacionException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos inválidos");
        }
        catch (NoEncontradoException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "No encontrado");
        }
        catch (ConflictoException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflicto");
        }
    }

    /// <summary>
    /// Adjudicar una cotización y emitir la Orden de Compra (Roles: Comprador, Admin).
    /// </summary>
    [HttpPost("{licitacionId}/adjudicar")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Adjudicar(
        Guid licitacionId,
        [FromBody] AdjudicarCotizacionRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var command = new AdjudicarCotizacionCommand(licitacionId, request.CotizacionId);
            var resultado = await _mediator.Send(command, cancellationToken);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Recurso no encontrado");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflicto en la adjudicación");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud inválida");
        }
    }
}

public record AdjudicarCotizacionRequestDto(Guid CotizacionId);
