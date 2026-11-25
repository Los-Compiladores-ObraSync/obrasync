using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/proveedores")]
[Route("api/cotizaciones/proveedores")]
[Authorize]
public class ReputacionProveedoresController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public ReputacionProveedoresController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Cargar o actualizar la calificación global de un proveedor (1 a 5) (Rol: Comprador).
    /// </summary>
    [HttpPut("{proveedorId:guid}/calificacion")]
    [Authorize(Roles = "Comprador")]
    [MapExceptionProblem(typeof(ArgumentOutOfRangeException), StatusCodes.Status400BadRequest, "Calificación fuera de rango")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Datos de calificación inválidos")]
    [ProducesResponseType(typeof(CalificacionProveedorResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpsertCalificacionProveedor(
        [FromRoute] Guid proveedorId,
        [FromBody] UpsertCalificacionProveedorRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (proveedorId == Guid.Empty)
        {
            return Problem(
                detail: "El identificador del proveedor (proveedorId) es obligatorio y debe ser un GUID válido.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro proveedorId inválido");
        }

        if (request is null)
        {
            return Problem(
                detail: "El cuerpo de la petición es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Petición inválida");
        }

        // Cada usuario tiene una sola calificación por proveedor: se identifica por el 'sub' del token.
        if (!TryObtenerUsuarioId(out var calificadorId))
        {
            return UsuarioNoIdentificadoProblem(
                detail: "No se pudo identificar al usuario que califica (claim 'sub' inválido).");
        }

        var command = new UpsertCalificacionProveedorCommand(
            proveedorId,
            calificadorId,
            request.Calificacion);

        var resultado = await _mediator.Send(command, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Consultar la calificación actual de un proveedor: el promedio de las calificaciones recibidas
    /// (Roles: Comprador, Admin; y el Proveedor, solo la suya).
    /// </summary>
    [HttpGet("{proveedorId:guid}/calificacion")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Calificación de proveedor no encontrada")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Parámetro proveedorId inválido")]
    [ProducesResponseType(typeof(CalificacionProveedorResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCalificacionProveedor(
        [FromRoute] Guid proveedorId,
        CancellationToken cancellationToken = default)
    {
        if (proveedorId == Guid.Empty)
        {
            return Problem(
                detail: "El identificador del proveedor (proveedorId) es obligatorio y debe ser un GUID válido.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro proveedorId inválido");
        }

        // Un Proveedor solo puede ver SU calificación (no la de la competencia).
        if (User.IsInRole("Proveedor") && !User.IsInRole("Admin") && !User.IsInRole("Comprador")
            && (!TryObtenerUsuarioId(out var usuarioId) || usuarioId != proveedorId))
        {
            return Problem(
                detail: "Un proveedor solo puede consultar su propia calificación.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado");
        }

        var query = new GetCalificacionProveedorQuery(proveedorId);
        var resultado = await _mediator.Send(query, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Consultar la reputación propia del proveedor autenticado (promedio, cantidad y últimos comentarios) (Rol: Proveedor).
    /// </summary>
    [HttpGet("/api/cotizaciones/proveedores/mi-reputacion")]
    [HttpGet("/api/ordenes-compra/mi-reputacion")]
    [HttpGet("/api/licitaciones/ordenes-compra/mi-reputacion")]
    [Authorize(Roles = "Proveedor")]
    [ProducesResponseType(typeof(ReputacionProveedorResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMiReputacionProveedor(
        [FromQuery] Guid? proveedorId = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al proveedor.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Proveedor no identificado");
        }

        if (proveedorId.HasValue && proveedorId.Value != Guid.Empty && proveedorId.Value != usuarioId)
        {
            return Problem(
                detail: "Un proveedor solo puede consultar su propia reputación.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado");
        }

        var resultado = await _mediator.Send(new GetMiReputacionProveedorQuery(usuarioId), cancellationToken);
        return Ok(resultado);
    }
}
