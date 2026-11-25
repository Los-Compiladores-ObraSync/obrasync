using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Commands;
using ObraSync.Licitaciones.Application.UseCases.OrdenesCompra.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/ordenes-compra")]
[Route("api/licitaciones/ordenes-compra")]
[Authorize]
public class OrdenesCompraController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public OrdenesCompraController(IMediator mediator)
    {
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
        if (!TryCrearPaginacion(pagina, tamano, out var paginacion, out var errorResult))
        {
            return errorResult!;
        }

        Guid? usuarioId = TryObtenerUsuarioId(out var idToken) ? idToken : null;

        var query = new GetOrdenesCompraQuery(
            ObraId: obraId,
            LicitacionId: licitacionId,
            ProveedorId: proveedorId,
            Estado: estado,
            RolUsuario: ObtenerRolConsulta(),
            UsuarioId: usuarioId,
            Paginacion: paginacion);

        var resultado = await _mediator.Send(query, cancellationToken);

        EscribirHeadersPaginacion(resultado.Total, paginacion);
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

        var resultado = await _mediator.Send(new GetPreciosHistoricosQuery(articuloIds), cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Registrar el despacho de una orden de compra y publicar OrdenCompraDespachadaEvent
    /// (Rol: Proveedor dueño de la orden). Soporta el re-despacho del remanente tras una recepción parcial (#248).
    /// POST /api/ordenes-compra/{id}/despachos
    /// </summary>
    [HttpPost("{id:guid}/despachos")]
    [Authorize(Roles = "Proveedor")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "Conflicto al despachar la orden de compra")]
    [ProducesResponseType(typeof(OrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Despachar(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DespacharOrdenCompraRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al proveedor.");
        }

        var command = new DespacharOrdenCompraCommand(
            OrdenCompraId: id,
            UsuarioId: usuarioId,
            CorrelationId: ObtenerCorrelationId(),
            FechaEstimadaLlegada: request?.FechaEstimadaLlegada,
            Observacion: request?.Observacion);

        var resultado = await _mediator.Send(command, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Registrar manualmente la resolución del problema abierto de una orden de compra
    /// (Roles: Comprador, Admin). Operación idempotente.
    /// POST /api/ordenes-compra/{id}/problema/resoluciones
    /// </summary>
    [HttpPost("{id:guid}/problema/resoluciones")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(ProblemaOrdenEstadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolverProblemaOrden(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem();
        }

        var dto = await _mediator.Send(
            new ResolverProblemaOrdenCompraCommand(
                OrdenCompraId: id,
                UsuarioId: usuarioId,
                RolUsuario: ObtenerRolUsuario(),
                CorrelationId: ObtenerCorrelationId()),
            cancellationToken);

        return Ok(dto);
    }
}
