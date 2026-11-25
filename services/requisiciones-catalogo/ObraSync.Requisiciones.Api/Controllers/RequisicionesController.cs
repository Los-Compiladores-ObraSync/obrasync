using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Api.Controllers;

[Route("api/requisiciones")]
[Authorize]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public class RequisicionesController : ApiControllerBase
{
    private readonly CreateRequisicionCommandHandler _createHandler;
    private readonly GetRequisicionesQueryHandler _getRequisicionesHandler;
    private readonly GetRequisicionByIdQueryHandler _getByIdHandler;
    private readonly AprobarRequisicionCommandHandler _aprobarHandler;
    private readonly RechazarRequisicionCommandHandler _rechazarHandler;
    private readonly ReenviarRequisicionCommandHandler _reenviarHandler;
    private readonly CancelarRequisicionCommandHandler _cancelarHandler;

    public RequisicionesController(
        CreateRequisicionCommandHandler createHandler,
        GetRequisicionesQueryHandler getRequisicionesHandler,
        GetRequisicionByIdQueryHandler getByIdHandler,
        AprobarRequisicionCommandHandler aprobarHandler,
        RechazarRequisicionCommandHandler rechazarHandler,
        ReenviarRequisicionCommandHandler reenviarHandler,
        CancelarRequisicionCommandHandler cancelarHandler)
    {
        _createHandler = createHandler;
        _getRequisicionesHandler = getRequisicionesHandler;
        _getByIdHandler = getByIdHandler;
        _aprobarHandler = aprobarHandler;
        _rechazarHandler = rechazarHandler;
        _reenviarHandler = reenviarHandler;
        _cancelarHandler = cancelarHandler;
    }

    /// <summary>
    /// Listar requisiciones con filtros opcionales (Roles: Capataz, Comprador, Admin)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [ProducesResponseType(typeof(IEnumerable<RequisicionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RequisicionResponseDto>>> ListarRequisiciones(
        [FromQuery] Guid? obraId = null,
        [FromQuery] string? estado = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        var paginacion = ParametrosPagina.Crear(pagina, tamano);
        var query = new GetRequisicionesQuery(obraId, estado, ObtenerSolicitanteRestringidoId(), paginacion);
        var resultado = await _getRequisicionesHandler.Handle(query, cancellationToken);
        return OkPaginado(resultado, paginacion);
    }

    /// <summary>
    /// Obtener el detalle de una requisición por su identificador (Roles: Capataz, Comprador, Admin)
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequisicionResponseDto>> ObtenerPorId(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetRequisicionByIdQuery(id, ObtenerSolicitanteRestringidoId());
        return Ok(await _getByIdHandler.Handle(query, cancellationToken));
    }

    /// <summary>
    /// Crear una nueva requisición de materiales para una obra (Roles: Capataz, Admin)
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Capataz,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RequisicionResponseDto>> CrearRequisicion(
        [FromBody] CrearRequisicionRequestDto request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        var command = request.ToCreateCommand(
            solicitanteId: ObtenerUsuarioId(),
            idempotencyKey: idempotencyKey,
            validarObraAsignada: EsCapatazSinPrivilegiosSuperiores(),
            authorizationHeader: ObtenerAuthorizationHeader(),
            correlationId: ObtenerCorrelationId().ToString());

        var creada = await _createHandler.Handle(command, cancellationToken);
        return CreatedAtAction(nameof(ObtenerPorId), new { id = creada.RequisicionId }, creada);
    }

    /// <summary>
    /// Editar una requisición pendiente, o corregir y reenviar una rechazada conservando su número y el historial de rechazos (Roles: Capataz dueño, Admin)
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Capataz,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RequisicionResponseDto>> EditarReenviarRequisicion(
        Guid id,
        [FromBody] EditarRequisicionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var command = request.ToReenviarCommand(id, ObtenerUsuarioId(), User.IsInRole("Admin"));
        return Ok(await _reenviarHandler.Handle(command, cancellationToken));
    }

    /// <summary>
    /// Registrar la cancelación de una requisición pendiente antes de que Compras la apruebe (Roles: Capataz dueño, Admin).
    /// POST /api/requisiciones/{id}/cancelaciones
    /// </summary>
    [HttpPost("{id:guid}/cancelaciones")]
    [Authorize(Roles = "Capataz,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RequisicionResponseDto>> CancelarRequisicion(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new CancelarRequisicionCommand(id, ObtenerUsuarioId(), User.IsInRole("Admin"));
        return Ok(await _cancelarHandler.Handle(command, cancellationToken));
    }

    /// <summary>
    /// Registrar la aprobación de una requisición pendiente y publicar RequisicionAprobadaEvent (Roles: Comprador, Admin).
    /// POST /api/requisiciones/{id}/aprobaciones
    /// </summary>
    [HttpPost("{id:guid}/aprobaciones")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RequisicionResponseDto>> AprobarRequisicion(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var command = new AprobarRequisicionCommand(id, ObtenerUsuarioId(), ObtenerCorrelationId());
        return Ok(await _aprobarHandler.Handle(command, cancellationToken));
    }

    /// <summary>
    /// Listar los motivos estandarizados permitidos para rechazar una requisición (#250).
    /// </summary>
    [HttpGet("motivos-rechazo")]
    [Authorize(Roles = "Comprador,Admin,Capataz")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetMotivosRechazo()
    {
        return Ok(MotivosRechazoRequisicion.Permitidos);
    }

    /// <summary>
    /// Registrar el rechazo de una requisición pendiente con motivo válido (Roles: Comprador, Admin) (#250).
    /// POST /api/requisiciones/{id}/rechazos
    /// </summary>
    [HttpPost("{id:guid}/rechazos")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RequisicionResponseDto>> RechazarRequisicion(
        Guid id,
        [FromBody] RechazarRequisicionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var command = request.ToRechazarCommand(id, ObtenerUsuarioId());
        return Ok(await _rechazarHandler.Handle(command, cancellationToken));
    }
}
