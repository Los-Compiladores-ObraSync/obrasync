using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[Route("api/cotizaciones")]
[Authorize]
public class CotizacionesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public CotizacionesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Listar las cotizaciones propias del proveedor autenticado y su resultado de adjudicación (Roles: Proveedor, Admin).
    /// </summary>
    [HttpGet("mias")]
    [Authorize(Roles = "Proveedor,Admin")]
    [ProducesResponseType(typeof(IEnumerable<MiCotizacionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMisCotizaciones(
        [FromQuery] Guid? proveedorId = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        if (!TryCrearPaginacion(pagina, tamano, out var paginacion, out var errorResult))
        {
            return errorResult!;
        }

        Guid proveedorIdConsulta;

        if (User.IsInRole("Admin") && proveedorId.HasValue && proveedorId.Value != Guid.Empty)
        {
            proveedorIdConsulta = proveedorId.Value;
        }
        else if (TryObtenerUsuarioId(out var usuarioIdToken))
        {
            proveedorIdConsulta = usuarioIdToken;
        }
        else
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al proveedor.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Proveedor no identificado");
        }

        var query = new GetMisCotizacionesQuery(proveedorIdConsulta, paginacion);
        var resultado = await _mediator.Send(query, cancellationToken);

        EscribirHeadersPaginacion(resultado.Total, paginacion);
        return Ok(resultado.Items);
    }

    /// <summary>
    /// Cargar una cotización para un pliego (Rol: Proveedor).
    /// proveedorId debe coincidir exactamente con el claim 'sub' del JWT.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Proveedor")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Licitación no encontrada")]
    [MapExceptionProblem(typeof(InvalidOperationException), StatusCodes.Status409Conflict, "Cotización duplicada")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Datos de cotización inválidos")]
    [ProducesResponseType(typeof(CotizacionResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CotizacionResponseDto>> CrearCotizacion(
        [FromBody] CrearCotizacionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioIdToken))
        {
            return UsuarioNoIdentificadoProblem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.");
        }

        // Seguridad crítica: un Proveedor no puede impersonar a otro Proveedor
        if (request.ProveedorId != usuarioIdToken)
        {
            return Problem(
                detail: "Un usuario con rol Proveedor solo puede presentar cotizaciones en su propio nombre (proveedorId debe coincidir con el claim 'sub' del token JWT).",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado por impersonación de proveedor");
        }

        var command = new CrearCotizacionCommand(
            request.LicitacionId,
            request.ProveedorId,
            request.Flete,
            request.PlazoEntregaDias,
            request.Items,
            User.FindFirstValue("name"));

        var creada = await _mediator.Send(command, cancellationToken);

        return CreatedAtAction(
            nameof(MatrizComparativaController.GetCotizaciones),
            "MatrizComparativa",
            new { licitacionId = creada.LicitacionId },
            creada);
    }

    /// <summary>
    /// Corregir una cotización propia mientras la licitación siga abierta y dentro de plazo (Rol: Proveedor dueño).
    /// Usa el mismo cuerpo que al cotizar; licitacionId y proveedorId del cuerpo se ignoran (valen los de la cotización guardada).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Proveedor")]
    [MapExceptionProblem(typeof(KeyNotFoundException), StatusCodes.Status404NotFound, "Cotización no encontrada")]
    [MapExceptionProblem(typeof(ArgumentException), StatusCodes.Status400BadRequest, "Datos de cotización inválidos")]
    [ProducesResponseType(typeof(CotizacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CotizacionResponseDto>> CorregirCotizacion(
        Guid id,
        [FromBody] CrearCotizacionRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!TryObtenerUsuarioId(out var usuarioId))
        {
            return UsuarioNoIdentificadoProblem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.");
        }

        var corregida = await _mediator.Send(
            new ReemplazarCotizacionCommand(
                id, usuarioId, false, request.Flete, request.PlazoEntregaDias, request.Items),
            cancellationToken);

        return Ok(corregida);
    }
}
