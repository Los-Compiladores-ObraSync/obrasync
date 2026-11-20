using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Exceptions;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Adjudicar.Commands;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Commands;
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
    private readonly GetSeguimientoRequisicionQueryHandler _seguimientoHandler;
    private readonly IMediator _mediator;

    public LicitacionesController(
        ILicitacionService service,
        GetLicitacionesQueryHandler getLicitacionesHandler,
        GetLicitacionByIdQueryHandler getByIdHandler,
        GetSeguimientoRequisicionQueryHandler seguimientoHandler,
        IMediator mediator)
    {
        _service = service;
        _getLicitacionesHandler = getLicitacionesHandler;
        _getByIdHandler = getByIdHandler;
        _seguimientoHandler = seguimientoHandler;
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
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(
                detail: error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Paginación inválida");
        }

        var rolUsuario = User.IsInRole("Proveedor")
            ? "Proveedor"
            : User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);

        // El 'sub' del token es el Id del proveedor: con él se marca qué pliegos ya cotizó.
        Guid? proveedorId = null;
        if (User.IsInRole("Proveedor"))
        {
            var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(sub, out var idToken) && idToken != Guid.Empty)
            {
                proveedorId = idToken;
            }
        }

        var query = new GetLicitacionesQuery(obraId, estado, rolUsuario, proveedorId, p);
        var resultado = await _getLicitacionesHandler.Handle(query, cancellationToken);

        Response.Headers["X-Total-Count"] = resultado.Total.ToString();
        Response.Headers["X-Page"] = p.Pagina.ToString();
        Response.Headers["X-Page-Size"] = p.Tamano.ToString();

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
        var dto = await _seguimientoHandler.Handle(new GetSeguimientoRequisicionQuery(requisicionId), cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// Obtener un pliego por Id (Roles: Comprador, Proveedor, Admin).
    /// Un Proveedor solo puede ver pliegos en estado Abierta; en otro estado recibe 404.
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
            var rolUsuario = User.IsInRole("Proveedor")
                ? "Proveedor"
                : User.FindFirstValue("role") ?? User.FindFirstValue(ClaimTypes.Role);

            var query = new GetLicitacionByIdQuery(id, rolUsuario);
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
            var correlationHeader = HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? HttpContext?.Items["CorrelationId"]?.ToString();

            var correlationId = Guid.TryParse(correlationHeader, out var parsedCorrelationId) && parsedCorrelationId != Guid.Empty
                ? parsedCorrelationId
                : Guid.NewGuid();

            var command = new AdjudicarCotizacionCommand(licitacionId, request.CotizacionId, correlationId);
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

    /// <summary>
    /// Extender el plazo de cotización de una licitación abierta (Roles: Comprador, Admin).
    /// </summary>
    [HttpPost("{id:guid}/extender")]
    [Authorize(Roles = "Comprador,Admin")]
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

        try
        {
            var resultado = await _mediator.Send(
                new ExtenderLicitacionCommand(id, request.FechaLimiteCotizacion), cancellationToken);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Recurso no encontrado");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Conflicto al extender la licitación");
        }
        catch (ArgumentException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Solicitud inválida");
        }
    }

    /// <summary>
    /// Cerrar o cancelar una licitación abierta sin adjudicar (Roles: Comprador, Admin).
    /// </summary>
    [HttpPost("{id:guid}/cerrar")]
    [Authorize(Roles = "Comprador,Admin")]
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
        try
        {
            var command = new CerrarLicitacionCommand(id, request?.Motivo);
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
                title: "Conflicto al cerrar la licitación");
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
