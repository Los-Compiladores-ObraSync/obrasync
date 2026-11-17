using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Queries;

namespace ObraSync.Licitaciones.Api.Controllers;

[ApiController]
[Route("api/cotizaciones")]
[Authorize]
public class CotizacionesController : ControllerBase
{
    private readonly CrearCotizacionCommandHandler _crearCotizacionHandler;
    private readonly GetCotizacionesByLicitacionIdQueryHandler _getCotizacionesHandler;
    private readonly GetMisCotizacionesQueryHandler _getMisCotizacionesHandler;

    public CotizacionesController(
        CrearCotizacionCommandHandler crearCotizacionHandler,
        GetCotizacionesByLicitacionIdQueryHandler getCotizacionesHandler,
        GetMisCotizacionesQueryHandler getMisCotizacionesHandler)
    {
        _crearCotizacionHandler = crearCotizacionHandler;
        _getCotizacionesHandler = getCotizacionesHandler;
        _getMisCotizacionesHandler = getMisCotizacionesHandler;
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
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(
                detail: error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Paginación inválida");
        }

        Guid proveedorIdConsulta;

        if (User.IsInRole("Admin") && proveedorId.HasValue && proveedorId.Value != Guid.Empty)
        {
            proveedorIdConsulta = proveedorId.Value;
        }
        else if (TryGetUserId(out var usuarioIdToken))
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

        var query = new GetMisCotizacionesQuery(proveedorIdConsulta, p);
        var resultado = await _getMisCotizacionesHandler.Handle(query, cancellationToken);

        Response.Headers["X-Total-Count"] = resultado.Total.ToString();
        Response.Headers["X-Page"] = p.Pagina.ToString();
        Response.Headers["X-Page-Size"] = p.Tamano.ToString();

        return Ok(resultado.Items);
    }

    /// <summary>
    /// Listar las cotizaciones de un pliego para la matriz comparativa (Roles: Comprador, Admin).
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(IEnumerable<CotizacionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCotizaciones(
        [FromQuery] Guid? licitacionId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        if (!licitacionId.HasValue || licitacionId.Value == Guid.Empty)
        {
            return Problem(
                detail: "El parámetro de consulta 'licitacionId' es obligatorio y debe ser un GUID válido.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro licitacionId inválido");
        }

        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(
                detail: error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Paginación inválida");
        }

        try
        {
            var query = new GetCotizacionesByLicitacionIdQuery(licitacionId.Value, p);
            var resultado = await _getCotizacionesHandler.Handle(query, cancellationToken);

            Response.Headers["X-Total-Count"] = resultado.Total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();

            return Ok(resultado.Items);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Licitación no encontrada");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro licitacionId inválido");
        }
    }

    /// <summary>
    /// Cargar una cotización para un pliego (Roles: Proveedor, Admin).
    /// Si el usuario es Proveedor, proveedorId debe coincidir exactamente con el claim 'sub' del JWT.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Proveedor,Admin")]
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
        if (!TryGetUserId(out var usuarioIdToken))
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        // Seguridad crítica: un Proveedor no puede impersonar a otro Proveedor
        if (User.IsInRole("Proveedor") && !User.IsInRole("Admin") && request.ProveedorId != usuarioIdToken)
        {
            return Problem(
                detail: "Un usuario con rol Proveedor solo puede presentar cotizaciones en su propio nombre (proveedorId debe coincidir con el claim 'sub' del token JWT).",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado por impersonación de proveedor");
        }

        try
        {
            var command = new CrearCotizacionCommand(
                request.LicitacionId,
                request.ProveedorId,
                request.Flete,
                request.PlazoEntregaDias,
                request.Items,
                // El nombre sale del JWT solo cuando la cotización es del propio usuario. Si un Admin carga la oferta de otro
                // proveedor, el claim "name" sería el del Admin: se deja en null antes que mostrar un nombre equivocado.
                request.ProveedorId == usuarioIdToken ? User.FindFirstValue("name") : null);

            var creada = await _crearCotizacionHandler.Handle(command, cancellationToken);

            return CreatedAtAction(
                nameof(GetCotizaciones),
                new { licitacionId = creada.LicitacionId },
                creada);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Licitación no encontrada");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Cotización duplicada");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de cotización inválidos");
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var subClaim = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
    }
}
