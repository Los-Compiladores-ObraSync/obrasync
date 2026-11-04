using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/licitaciones")]
[Authorize(Roles = "Comprador,Proveedor,Admin")]
public class LicitacionesController : ControllerBase
{
    private readonly GetLicitacionesQueryHandler _getLicitacionesHandler;
    private readonly GetLicitacionByIdQueryHandler _getByIdHandler;

    public LicitacionesController(
        GetLicitacionesQueryHandler getLicitacionesHandler,
        GetLicitacionByIdQueryHandler getByIdHandler)
    {
        _getLicitacionesHandler = getLicitacionesHandler;
        _getByIdHandler = getByIdHandler;
    }

    /// <summary>
    /// Listar pliegos con filtros opcionales (Roles: Comprador, Proveedor, Admin).
    /// Un Proveedor solo visualiza pliegos en estado Abierta.
    /// </summary>
    [HttpGet]
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
}
