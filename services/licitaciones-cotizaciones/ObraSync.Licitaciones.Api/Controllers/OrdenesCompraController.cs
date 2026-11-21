using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Handlers;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/ordenes-compra")]
[Route("api/licitaciones/ordenes-compra")]
[Authorize]
public class OrdenesCompraController : ControllerBase
{
    private readonly GetOrdenesCompraQueryHandler _getOrdenesCompraHandler;
    private readonly IMediator _mediator;

    public OrdenesCompraController(
        GetOrdenesCompraQueryHandler getOrdenesCompraHandler,
        IMediator mediator)
    {
        _getOrdenesCompraHandler = getOrdenesCompraHandler;
        _mediator = mediator;
    }

    /// <summary>
    /// Listar órdenes de compra emitidas y despachadas con paginación y filtros opcionales.
    /// Si quien consulta es un Proveedor, solo visualiza sus propias órdenes de compra.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(IReadOnlyList<OrdenCompraResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<OrdenCompraResponseDto>>> Listar(
        [FromQuery] Guid? obraId = null,
        [FromQuery] Guid? licitacionId = null,
        [FromQuery] Guid? proveedorId = null,
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

        Guid? usuarioId = null;
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var idToken) && idToken != Guid.Empty)
        {
            usuarioId = idToken;
        }

        var query = new GetOrdenesCompraQuery(
            ObraId: obraId,
            LicitacionId: licitacionId,
            ProveedorId: proveedorId,
            Estado: estado,
            RolUsuario: rolUsuario,
            UsuarioId: usuarioId,
            Paginacion: p);

        var resultado = await _getOrdenesCompraHandler.Handle(query, cancellationToken);

        Response.Headers["X-Total-Count"] = resultado.Total.ToString();
        Response.Headers["X-Page"] = p.Pagina.ToString();
        Response.Headers["X-Page-Size"] = p.Tamano.ToString();

        return Ok(resultado.Items);
    }

    /// <summary>
    /// Último precio unitario pagado por cada artículo pedido, según las órdenes de compra ya emitidas
    /// (Roles: Comprador, Admin). Un artículo que nunca se compró no aparece en la respuesta.
    /// </summary>
    [HttpGet("precios-historicos")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<PrecioHistoricoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PrecioHistoricoDto>>> PreciosHistoricos(
        [FromQuery] Guid[]? articuloIds,
        [FromServices] IOrdenCompraRepository repositorio,
        CancellationToken cancellationToken = default)
    {
        const int maximo = 50;
        if (articuloIds is null || articuloIds.Length == 0 || articuloIds.Length > maximo || articuloIds.Any(id => id == Guid.Empty))
        {
            return Problem(
                detail: $"Indicá entre 1 y {maximo} identificadores de artículo válidos (articuloIds).",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Solicitud inválida");
        }

        return Ok(await repositorio.GetUltimosPreciosAsync(articuloIds, cancellationToken));
    }

    /// <summary>
    /// Marcar una orden de compra como despachada y publicar OrdenCompraDespachadaEvent
    /// (Rol: Proveedor dueño de la orden). Operación idempotente.
    /// </summary>
    [HttpPost("{id:guid}/despachar")]
    [Authorize(Roles = "Proveedor")]
    [ProducesResponseType(typeof(OrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Despachar(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DespacharOrdenCompraRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var usuarioId) || usuarioId == Guid.Empty)
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al proveedor.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        try
        {
            var correlationHeader = HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? HttpContext?.Items["CorrelationId"]?.ToString();

            var correlationId = Guid.TryParse(correlationHeader, out var parsedCorrelationId) && parsedCorrelationId != Guid.Empty
                ? parsedCorrelationId
                : Guid.NewGuid();

            var command = new DespacharOrdenCompraCommand(
                OrdenCompraId: id,
                UsuarioId: usuarioId,
                CorrelationId: correlationId,
                FechaEstimadaLlegada: request?.FechaEstimadaLlegada,
                Observacion: request?.Observacion);
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
        catch (UnauthorizedAccessException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado");
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
