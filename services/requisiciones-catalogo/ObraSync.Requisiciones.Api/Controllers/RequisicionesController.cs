using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Requisiciones.Api.Middlewares;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
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
        private readonly RechazarRequisicionCommandHandler _rechazarHandler;
        private readonly ReenviarRequisicionCommandHandler? _reenviarHandler;
        private readonly IObraAsignacionValidator? _obraAsignacionValidator;

        public RequisicionesController(
            CreateRequisicionCommandHandler createHandler,
            GetRequisicionesQueryHandler getRequisicionesHandler,
            GetRequisicionByIdQueryHandler getByIdHandler,
            AprobarRequisicionCommandHandler aprobarHandler,
            RechazarRequisicionCommandHandler rechazarHandler,
            IObraAsignacionValidator? obraAsignacionValidator = null,
            ReenviarRequisicionCommandHandler? reenviarHandler = null)
        {
            _createHandler = createHandler;
            _getRequisicionesHandler = getRequisicionesHandler;
            _getByIdHandler = getByIdHandler;
            _aprobarHandler = aprobarHandler;
            _rechazarHandler = rechazarHandler;
            _obraAsignacionValidator = obraAsignacionValidator;
            _reenviarHandler = reenviarHandler;
        }

        /// <summary>
        /// Listar requisiciones con filtros opcionales (Roles: Capataz, Comprador, Admin)
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Capataz,Comprador,Admin")]
        [ProducesResponseType(typeof(IEnumerable<RequisicionResponseDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        public async Task<ActionResult<IReadOnlyList<RequisicionResponseDto>>> ListarRequisiciones(
            [FromQuery] Guid? obraId = null,
            [FromQuery] string? estado = null,
            [FromQuery] int pagina = 1,
            [FromQuery] int tamano = 50,
            CancellationToken cancellationToken = default)
        {
            var p = new ParametrosPagina(pagina, tamano);
            var error = p.Validar();
            if (error is not null)
            {
                return Problem(
                    detail: error,
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Paginación inválida");
            }

            Guid? solicitanteId = null;
            if (EsCapatazSinPrivilegiosSuperiores())
            {
                if (!TryGetUserId(out var userId))
                {
                    return Problem(
                        detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Usuario no identificado");
                }
                solicitanteId = userId;
            }

            var query = new GetRequisicionesQuery(obraId, estado, solicitanteId, p);
            var resultado = await _getRequisicionesHandler.Handle(query, cancellationToken);

            Response.Headers["X-Total-Count"] = resultado.Total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();

            return Ok(resultado.Items);
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
            Guid? solicitanteIdRestriccion = null;
            if (EsCapatazSinPrivilegiosSuperiores())
            {
                if (!TryGetUserId(out var userId))
                {
                    return Problem(
                        detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Usuario no identificado");
                }
                solicitanteIdRestriccion = userId;
            }

            var query = new GetRequisicionByIdQuery(id, solicitanteIdRestriccion);
            var requisicion = await _getByIdHandler.Handle(query, cancellationToken);
            return Ok(requisicion);
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
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
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

            // #155: el Capataz solo puede crear requisiciones para obras que tiene asignadas
            if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
            {
                var authHeader = Request?.Headers["Authorization"].ToString();
                var asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    request.ObraId,
                    authHeader,
                    GetCorrelationId().ToString(),
                    cancellationToken);

                if (!asignada)
                {
                    return Problem(
                        detail: "El capataz autenticado no tiene asignada la obra indicada para crear requisiciones.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Obra no asignada");
                }
            }

            var keyEfectiva = !string.IsNullOrWhiteSpace(idempotencyKey)
                ? idempotencyKey
                : Request?.Headers["Idempotency-Key"].ToString();

            var creada = await _createHandler.Handle(
                request.ToCreateCommand(solicitanteId, keyEfectiva),
                cancellationToken);

            return CreatedAtAction(
                nameof(ObtenerPorId),
                new { id = creada.RequisicionId },
                creada);
        }

        /// <summary>
        /// Editar y reenviar una requisición rechazada conservando su número y el historial de rechazos (Roles: Capataz dueño, Admin)
        /// </summary>
        [HttpPut("{id:guid}")]
        [Authorize(Roles = "Capataz,Admin")]
        [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RequisicionResponseDto>> EditarReenviarRequisicion(
            Guid id,
            [FromBody] EditarRequisicionRequestDto? request,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetUserId(out var usuarioId))
            {
                return Problem(
                    detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado");
            }

            if (request == null || request.Items == null || request.Items.Count == 0)
            {
                return Problem(
                    detail: "La requisición debe contener al menos un ítem para poder reenviarse.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Datos de requisición inválidos");
            }

            var handler = _reenviarHandler
                ?? HttpContext?.RequestServices.GetService(typeof(ReenviarRequisicionCommandHandler)) as ReenviarRequisicionCommandHandler
                ?? throw new NotSupportedException("No se encontró el servicio ReenviarRequisicionCommandHandler.");

            var reenviada = await handler.Handle(
                request.ToReenviarCommand(id, usuarioId, User.IsInRole("Admin")),
                cancellationToken);

            return Ok(reenviada);
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

            var command = new AprobarRequisicionCommand(id, aprobadorId, GetCorrelationId());
            var aprobada = await _aprobarHandler.Handle(command, cancellationToken);
            return Ok(aprobada);
        }

        /// <summary>
        /// Rechazar una requisición pendiente con motivo obligatorio (Roles: Comprador, Admin)
        /// </summary>
        [HttpPost("{id:guid}/rechazar")]
        [Authorize(Roles = "Comprador,Admin")]
        [ProducesResponseType(typeof(RequisicionResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
        public async Task<ActionResult<RequisicionResponseDto>> RechazarRequisicion(
            Guid id,
            [FromBody] RechazarRequisicionRequestDto? request,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetUserId(out var rechazadorId))
            {
                return Problem(
                    detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Usuario no identificado");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Motivo))
            {
                return Problem(
                    detail: "El motivo de rechazo de la requisición es obligatorio.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Motivo de rechazo inválido");
            }

            var command = new RechazarRequisicionCommand(id, rechazadorId, request.Motivo);
            var rechazada = await _rechazarHandler.Handle(command, cancellationToken);
            return Ok(rechazada);
        }

        private bool EsCapatazSinPrivilegiosSuperiores() =>
            User.IsInRole("Capataz") && !User.IsInRole("Comprador") && !User.IsInRole("Admin");

        private bool TryGetUserId(out Guid userId)
        {
            var subClaim = User.FindFirstValue("sub")
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
        }

        private Guid GetCorrelationId()
        {
            var header = HttpContext?.Items[CorrelationIdMiddleware.CorrelationIdItemKey] as string
                ?? Request.Headers[CorrelationIdMiddleware.CorrelationIdHeader].ToString();
            return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
                ? correlationId
                : Guid.NewGuid();
        }
    }
}
