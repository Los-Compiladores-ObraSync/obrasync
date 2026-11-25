using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/licitaciones")]
[Authorize]
public class LicitacionesController : ApiControllerBase
{
    private readonly ILicitacionService _service;
    private readonly IMediator _mediator;

    public LicitacionesController(
        ILicitacionService service,
        IMediator mediator)
    {
        _service = service;
        _mediator = mediator;
    }

    /// <summary>
    /// Listar pliegos con filtros opcionales (Roles: Comprador, Proveedor, Admin).
    /// Un Proveedor solo visualiza pliegos en estado Abierta, y cada uno trae yaCotizada (true/false);
    /// para Comprador y Admin ese campo viene en null.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Comprador,Proveedor,Admin")]
    [ProducesResponseType(typeof(IEnumerable<LicitacionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LicitacionResponseDto>>> ListarLicitaciones(
        [FromQuery] Guid? obraId = null,
        [FromQuery] string? estado = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryCrearPaginacion(pagina, tamano, out var paginacion, out var errorResult))
        {
            return errorResult!;
        }

        var rolUsuario = ObtenerRolConsulta();

        // El 'sub' del token es el Id del proveedor: con él se marca qué pliegos ya cotizó.
        Guid? proveedorId = null;
        if (User.IsInRole("Proveedor") && TryObtenerUsuarioId(out var idToken))
        {
            proveedorId = idToken;
        }

        var query = new GetLicitacionesQuery(obraId, estado, rolUsuario, proveedorId, paginacion);
        var resultado = await _mediator.Send(query, cancellationToken);

        EscribirHeadersPaginacion(resultado.Total, paginacion);
        return Ok(resultado.Items);
    }

    /// <summary>
    /// Seguimiento de una requisición aprobada: licitación, ofertas recibidas y órdenes de compra (Roles: Capataz, Comprador, Admin).
    /// Si todavía no hay licitación responde 200 con los campos vacíos. Para el Capataz, el dueño de la requisición
    /// se verifica en el servicio de Requisiciones (la pantalla consulta primero la requisición, que sí filtra por dueño).
    /// </summary>
    [HttpGet("seguimiento/{requisicionId:guid}")]
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [ProducesResponseType(typeof(SeguimientoRequisicionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SeguimientoRequisicionDto>> Seguimiento(
        Guid requisicionId,
        CancellationToken cancellationToken = default)
    {
        var dto = await _mediator.Send(new GetSeguimientoRequisicionQuery(requisicionId), cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// Obtener un pliego por Id (Roles: Comprador, Proveedor, Admin).
    /// Un Proveedor solo puede ver pliegos en estado Abierta; en otro estado recibe 404.
    /// </summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Comprador,Proveedor,Admin")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Licitación no encontrada")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LicitacionResponseDto>> ObtenerPorId(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var query = new GetLicitacionByIdQuery(id, ObtenerRolConsulta());
        var licitacion = await _mediator.Send(query, cancellationToken);
        return Ok(licitacion);
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
        var licitacion = await _service.CrearAsync(request, ct);
        return CreatedAtAction(
            nameof(ObtenerPorId),
            new { id = licitacion.LicitacionId },
            licitacion);
    }

    /// <summary>
    /// Registrar la adjudicación de una cotización y emitir la Orden de Compra (Roles: Comprador, Admin).
    /// POST /api/licitaciones/{licitacionId}/adjudicaciones
    /// </summary>
    [HttpPost("{licitacionId}/adjudicaciones")]
    [Authorize(Roles = "Comprador,Admin")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "Conflicto en la adjudicación")]
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
        var command = new AdjudicarCotizacionCommand(licitacionId, request.CotizacionId, ObtenerCorrelationId());
        var resultado = await _mediator.Send(command, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Registrar una extensión del plazo de cotización de una licitación abierta (Roles: Comprador, Admin).
    /// POST /api/licitaciones/{id}/extensiones
    /// </summary>
    [HttpPost("{id:guid}/extensiones")]
    [Authorize(Roles = "Comprador,Admin")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "Conflicto al extender la licitación")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Extender(
        Guid id,
        [FromBody] ExtenderLicitacionRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.FechaLimiteCotizacion == default)
        {
            return Problem(
                detail: "La nueva fecha límite de cotización es obligatoria.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud inválida");
        }

        var resultado = await _mediator.Send(
            new ExtenderLicitacionCommand(id, request.FechaLimiteCotizacion),
            cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Registrar el cierre o cancelación de una licitación abierta sin adjudicar (Roles: Comprador, Admin).
    /// POST /api/licitaciones/{id}/cierres
    /// </summary>
    [HttpPost("{id:guid}/cierres")]
    [Authorize(Roles = "Comprador,Admin")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "Conflicto al cerrar la licitación")]
    [ProducesResponseType(typeof(LicitacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cerrar(
        Guid id,
        [FromBody] CerrarLicitacionRequestDto? request,
        CancellationToken cancellationToken)
    {
        var command = new CerrarLicitacionCommand(id, request?.Motivo);
        var resultado = await _mediator.Send(command, cancellationToken);
        return Ok(resultado);
    }
}

public record AdjudicarCotizacionRequestDto(Guid CotizacionId);
