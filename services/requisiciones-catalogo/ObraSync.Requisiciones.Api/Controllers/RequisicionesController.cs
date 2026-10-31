using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Exceptions;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries;

namespace ObraSync.Requisiciones.Api.Controllers
{
    [ApiController]
    [Route("api/requisiciones")]
    [Authorize]
    public class RequisicionesController : ControllerBase
    {
        private readonly CreateRequisicionCommandHandler _createHandler;
        private readonly GetRequisicionesQueryHandler _getRequisicionesHandler;
        private readonly GetRequisicionByIdQueryHandler _getByIdHandler;
        private readonly AprobarRequisicionCommandHandler _aprobarHandler;

        public RequisicionesController(
            CreateRequisicionCommandHandler createHandler,
            GetRequisicionesQueryHandler getRequisicionesHandler,
            GetRequisicionByIdQueryHandler getByIdHandler,
            AprobarRequisicionCommandHandler aprobarHandler)
        {
            _createHandler = createHandler;
            _getRequisicionesHandler = getRequisicionesHandler;
            _getByIdHandler = getByIdHandler;
            _aprobarHandler = aprobarHandler;
        }

        /// <summary>
        /// Listar requisiciones con filtros opcionales (Roles: Capataz, Comprador, Admin)
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Capataz,Comprador,Admin")]
        [ProducesResponseType(typeof(IEnumerable<RequisicionResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<RequisicionResponseDto>>> ListarRequisiciones(
            [FromQuery] Guid? obraId = null,
            [FromQuery] string? estado = null,
            CancellationToken cancellationToken = default)
        {
            var query = new GetRequisicionesQuery(obraId, estado);
            var requisiciones = await _getRequisicionesHandler.Handle(query, cancellationToken);
            return Ok(requisiciones);
        }

        /// <summary>
        /// Obtener el detalle de una requisición por su identificador (Roles: Capataz, Comprador, Admin)
        /// </summary>
        [HttpGet("{id:guid}")]
        [Authorize(Roles = "Capataz,Comprador,Admin")]
        [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<RequisicionResponseDto>> ObtenerPorId(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var query = new GetRequisicionByIdQuery(id);
                var requisicion = await _getByIdHandler.Handle(query, cancellationToken);
                return Ok(requisicion);
            }
            catch (KeyNotFoundException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Requisición no encontrada");
            }
        }

        /// <summary>
        /// Crear una nueva requisición de materiales para una obra (Roles: Capataz, Admin)
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Capataz,Admin")]
        [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RequisicionResponseDto>> CrearRequisicion(
            [FromBody] CrearRequisicionRequestDto request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetUserId(out var solicitanteId))
            {
                return Problem(
                    detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado");
            }

            var keyEfectiva = !string.IsNullOrWhiteSpace(idempotencyKey)
                ? idempotencyKey
                : Request?.Headers["Idempotency-Key"].ToString();

            try
            {
                var command = new CreateRequisicionCommand(
                    request.ObraId,
                    solicitanteId,
                    request.Items,
                    request.Etapa,
                    request.Observaciones,
                    keyEfectiva);

                var creada = await _createHandler.Handle(command, cancellationToken);

                return CreatedAtAction(
                    nameof(ObtenerPorId),
                    new { id = creada.RequisicionId },
                    creada);
            }
            catch (RequisicionDuplicadaException ex)
            {
                var result = Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Requisición duplicada");

                if (result.Value is ProblemDetails problemDetails)
                {
                    problemDetails.Extensions["numeroRequisicion"] = ex.NumeroRequisicionExistente;
                    problemDetails.Extensions["requisicionId"] = ex.RequisicionExistenteId;
                }

                return result;
            }
            catch (KeyNotFoundException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Artículo de catálogo inexistente");
            }
            catch (ArgumentException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Datos de requisición inválidos");
            }
        }

        /// <summary>
        /// Aprobar una requisición pendiente y publicar RequisicionAprobadaEvent (Roles: Comprador, Admin)
        /// </summary>
        [HttpPost("{id:guid}/aprobar")]
        [Authorize(Roles = "Comprador,Admin")]
        [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RequisicionResponseDto>> AprobarRequisicion(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetUserId(out var aprobadorId))
            {
                return Problem(
                    detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado");
            }

            var correlationId = GetCorrelationId();

            try
            {
                var command = new AprobarRequisicionCommand(id, aprobadorId, correlationId);
                var aprobada = await _aprobarHandler.Handle(command, cancellationToken);
                return Ok(aprobada);
            }
            catch (KeyNotFoundException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Requisición no encontrada");
            }
            catch (InvalidOperationException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflicto de estado en la requisición");
            }
            catch (ArgumentException ex)
            {
                return Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Solicitud inválida");
            }
        }

        private bool TryGetUserId(out Guid userId)
        {
            var subClaim = User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
        }

        private Guid GetCorrelationId()
        {
            var header = Request.Headers["X-Correlation-ID"].ToString();
            return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
                ? correlationId
                : Guid.NewGuid();
        }
    }
}
