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
    private readonly ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers.UpsertCalificacionProveedorCommandHandler? _upsertCalificacionHandler;
    private readonly ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers.GetCalificacionProveedorQueryHandler? _getCalificacionHandler;

    public CotizacionesController(
        CrearCotizacionCommandHandler crearCotizacionHandler,
        GetCotizacionesByLicitacionIdQueryHandler getCotizacionesHandler,
        GetMisCotizacionesQueryHandler getMisCotizacionesHandler,
        ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers.UpsertCalificacionProveedorCommandHandler? upsertCalificacionHandler = null,
        ObraSync.Licitaciones.Application.UseCases.Calificaciones.Handlers.GetCalificacionProveedorQueryHandler? getCalificacionHandler = null)
    {
        _crearCotizacionHandler = crearCotizacionHandler;
        _getCotizacionesHandler = getCotizacionesHandler;
        _getMisCotizacionesHandler = getMisCotizacionesHandler;
        _upsertCalificacionHandler = upsertCalificacionHandler;
        _getCalificacionHandler = getCalificacionHandler;
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
        [FromQuery] decimal? pesoPrecio = null,
        [FromQuery] decimal? pesoFlete = null,
        [FromQuery] decimal? pesoPlazo = null,
        [FromQuery] decimal? pesoCalificacion = null,
        [FromQuery] decimal? precio = null,
        [FromQuery] decimal? flete = null,
        [FromQuery] decimal? plazo = null,
        [FromQuery] decimal? calificacion = null,
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
            var query = new GetCotizacionesByLicitacionIdQuery(
                licitacionId.Value,
                p,
                PesoPrecio: pesoPrecio ?? precio,
                PesoFlete: pesoFlete ?? flete,
                PesoPlazo: pesoPlazo ?? plazo,
                PesoCalificacion: pesoCalificacion ?? calificacion);
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
                title: "Parámetros de consulta inválidos");
        }
    }

    /// <summary>
    /// Cargar o actualizar la calificación global de un proveedor (1 a 5) (Roles: Comprador, Admin).
    /// </summary>
    [HttpPut("proveedores/{proveedorId:guid}/calificacion")]
    [HttpPut("/api/proveedores/{proveedorId:guid}/calificacion")]
    [Authorize(Roles = "Comprador,Admin")]
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

        if (_upsertCalificacionHandler is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        // Cada usuario tiene una sola calificación por proveedor: se identifica por el 'sub' del token.
        if (!TryGetUserId(out var calificadorId))
        {
            return Problem(
                detail: "No se pudo identificar al usuario que califica (claim 'sub' inválido).",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        try
        {
            var command = new ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands.UpsertCalificacionProveedorCommand(
                proveedorId,
                calificadorId,
                request.Calificacion);
            var resultado = await _upsertCalificacionHandler.Handle(command, cancellationToken);
            return Ok(resultado);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Calificación fuera de rango");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Datos de calificación inválidos");
        }
    }

    /// <summary>
    /// Consultar la calificación actual de un proveedor: el promedio de las calificaciones recibidas
    /// (Roles: Comprador, Admin; y el Proveedor, solo la suya).
    /// </summary>
    [HttpGet("proveedores/{proveedorId:guid}/calificacion")]
    [HttpGet("/api/proveedores/{proveedorId:guid}/calificacion")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
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
            && (!TryGetUserId(out var usuarioId) || usuarioId != proveedorId))
        {
            return Problem(
                detail: "Un proveedor solo puede consultar su propia calificación.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Acceso denegado");
        }

        if (_getCalificacionHandler is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        try
        {
            var query = new ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries.GetCalificacionProveedorQuery(proveedorId);
            var resultado = await _getCalificacionHandler.Handle(query, cancellationToken);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Calificación de proveedor no encontrada");
        }
        catch (ArgumentException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Parámetro proveedorId inválido");
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

    /// <summary>
    /// Corregir una cotización propia mientras la licitación siga abierta y dentro de plazo (Roles: Proveedor dueño, Admin).
    /// Usa el mismo cuerpo que al cotizar; licitacionId y proveedorId del cuerpo se ignoran (valen los de la cotización guardada).
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Proveedor,Admin")]
    [ProducesResponseType(typeof(CotizacionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CotizacionResponseDto>> CorregirCotizacion(
        Guid id,
        [FromBody] CrearCotizacionRequestDto request,
        [FromServices] ReemplazarCotizacionCommandHandler handler,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var usuarioId))
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        try
        {
            var corregida = await handler.Handle(
                new ReemplazarCotizacionCommand(
                    id, usuarioId, User.IsInRole("Admin"), request.Flete, request.PlazoEntregaDias, request.Items),
                cancellationToken);
            return Ok(corregida);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Cotización no encontrada");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden, title: "Acceso denegado");
        }
        catch (ArgumentException ex)
        {
            return Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Datos de cotización inválidos");
        }
    }

    private bool TryGetUserId(out Guid userId)
    {
        var subClaim = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return Guid.TryParse(subClaim, out userId) && userId != Guid.Empty;
    }
}
