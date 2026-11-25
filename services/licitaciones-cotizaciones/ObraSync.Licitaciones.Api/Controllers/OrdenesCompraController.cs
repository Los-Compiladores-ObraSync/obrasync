using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ObraSync.Licitaciones.Api.Hubs;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Commands;
using ObraSync.Licitaciones.Application.UseCases.Calificaciones.Queries;
using ObraSync.Licitaciones.Application.UseCases.Chat.Commands;
using ObraSync.Licitaciones.Application.UseCases.Chat.Queries;
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
    /// Registrar el despacho de una orden de compra y publicar OrdenCompraDespachadaEvent
    /// (Rol: Proveedor dueño de la orden). Soporta el re-despacho del remanente tras una recepción parcial (#248).
    /// POST /api/ordenes-compra/{id}/despachos
    /// </summary>
    [HttpPost("{id:guid}/despachos")]
    [Authorize(Roles = "Proveedor")]
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
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflicto al despachar la orden de compra");
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
    /// Calificar o editar la calificación de una orden de compra recibida completa (Rol: Comprador).
    /// </summary>
    [HttpPut("{id:guid}/calificacion")]
    [Authorize(Roles = "Comprador")]
    [ProducesResponseType(typeof(CalificacionOrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpsertCalificacionOrden(
        [FromRoute] Guid id,
        [FromBody] UpsertCalificacionOrdenCompraRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Problem(
                detail: "El cuerpo de la petición es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Petición inválida");
        }

        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var usuarioId) || usuarioId == Guid.Empty)
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al usuario.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        try
        {
            var command = new UpsertCalificacionOrdenCompraCommand(
                OrdenCompraId: id,
                UsuarioId: usuarioId,
                Calificacion: request.Calificacion,
                Comentario: request.Comentario);
            var resultado = await _mediator.Send(command, cancellationToken);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Orden de compra no encontrada");
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "La orden de compra aún no está habilitada para calificar");
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
    /// Consultar la calificación que el usuario autenticado cargó para una orden de compra (Roles: Comprador, Admin).
    /// </summary>
    [HttpGet("{id:guid}/calificacion")]
    [Authorize(Roles = "Comprador,Admin")]
    [ProducesResponseType(typeof(CalificacionOrdenCompraResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMiCalificacionOrden(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var usuarioId) || usuarioId == Guid.Empty)
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al usuario.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        try
        {
            var query = new GetMiCalificacionOrdenCompraQuery(id, usuarioId);
            var resultado = await _mediator.Send(query, cancellationToken);
            return Ok(resultado);
        }
        catch (KeyNotFoundException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status404NotFound,
                title: "Calificación no encontrada");
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
    /// Consultar la reputación propia del proveedor autenticado (Rol: Proveedor).
    /// </summary>
    [HttpGet("mi-reputacion")]
    [Authorize(Roles = "Proveedor")]
    [ProducesResponseType(typeof(ReputacionProveedorResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMiReputacionProveedor(
        [FromQuery] Guid? proveedorId = null,
        CancellationToken cancellationToken = default)
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var usuarioId) || usuarioId == Guid.Empty)
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

    /// <summary>
    /// Listar los mensajes del chat de una orden de compra con paginación
    /// (Roles: Comprador, Admin, Proveedor dueño de la orden).
    /// </summary>
    [HttpGet("{id:guid}/chat/mensajes")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(IReadOnlyList<MensajeChatOrdenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListarMensajesChat(
        [FromRoute] Guid id,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = 50,
        CancellationToken cancellationToken = default)
    {
        var paginacion = new ParametrosPagina(pagina, tamano);
        var error = paginacion.Validar();
        if (error is not null)
        {
            return Problem(
                detail: error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Paginación inválida");
        }

        return await EjecutarAccionChatAsync(async (usuarioId, _, rolUsuario) =>
        {
            var resultado = await _mediator.Send(
                new GetMensajesChatOrdenQuery(id, usuarioId, rolUsuario, paginacion),
                cancellationToken);

            Response.Headers["X-Total-Count"] = resultado.Total.ToString();
            Response.Headers["X-Page"] = paginacion.Pagina.ToString();
            Response.Headers["X-Page-Size"] = paginacion.Tamano.ToString();

            return Ok(resultado.Items);
        });
    }

    /// <summary>
    /// Enviar un mensaje al chat de una orden de compra
    /// (Roles: Comprador, Proveedor dueño de la orden; Admin solo lectura).
    /// </summary>
    [HttpPost("{id:guid}/chat/mensajes")]
    [Authorize(Roles = "Comprador,Proveedor")]
    [ProducesResponseType(typeof(MensajeChatOrdenDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnviarMensajeChat(
        [FromRoute] Guid id,
        [FromBody] EnviarMensajeChatRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Problem(
                detail: "El cuerpo de la petición es obligatorio.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Petición inválida");
        }

        return await EjecutarAccionChatAsync(async (usuarioId, usuarioNombre, rolUsuario) =>
        {
            var command = new EnviarMensajeChatOrdenCommand(
                OrdenCompraId: id,
                UsuarioId: usuarioId,
                UsuarioNombre: usuarioNombre,
                RolUsuario: rolUsuario,
                Texto: request.Texto,
                CorrelationId: ObtenerCorrelationId());

            var dto = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(ListarMensajesChat), new { id }, dto);
        });
    }

    /// <summary>
    /// Registrar la lectura del chat de una orden de compra para el usuario autenticado
    /// (Roles: Comprador, Admin, Proveedor dueño de la orden). Operación idempotente.
    /// POST /api/ordenes-compra/{id}/chat/lecturas
    /// </summary>
    [HttpPost("{id:guid}/chat/lecturas")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(LecturaChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> MarcarChatLeido(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        return EjecutarAccionChatAsync(async (usuarioId, _, rolUsuario) =>
        {
            var dto = await _mediator.Send(
                new MarcarChatOrdenLeidoCommand(id, usuarioId, rolUsuario),
                cancellationToken);
            return Ok(dto);
        });
    }

    /// <summary>
    /// Consultar la cantidad de mensajes no leídos por orden de compra para el usuario actual
    /// (Roles: Comprador, Admin, Proveedor).
    /// </summary>
    [HttpGet("chat/no-leidos")]
    [Authorize(Roles = "Comprador,Admin,Proveedor")]
    [ProducesResponseType(typeof(ResumenNoLeidosChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public Task<IActionResult> ObtenerNoLeidosChat(CancellationToken cancellationToken = default)
    {
        return EjecutarAccionChatAsync(async (usuarioId, _, rolUsuario) =>
        {
            var dto = await _mediator.Send(
                new GetNoLeidosChatOrdenesQuery(usuarioId, rolUsuario),
                cancellationToken);
            return Ok(dto);
        });
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
    public Task<IActionResult> ResolverProblemaOrden(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        return EjecutarAccionChatAsync(async (usuarioId, _, rolUsuario) =>
        {
            var dto = await _mediator.Send(
                new ResolverProblemaOrdenCompraCommand(
                    OrdenCompraId: id,
                    UsuarioId: usuarioId,
                    RolUsuario: rolUsuario,
                    CorrelationId: ObtenerCorrelationId()),
                cancellationToken);
            return Ok(dto);
        });
    }

    private Guid ObtenerCorrelationId()
    {
        var correlationHeader = HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? HttpContext?.Items["CorrelationId"]?.ToString();

        return Guid.TryParse(correlationHeader, out var parsed) && parsed != Guid.Empty
            ? parsed
            : Guid.NewGuid();
    }

    private async Task<IActionResult> EjecutarAccionChatAsync(
        Func<Guid, string?, string?, Task<IActionResult>> accion)
    {
        if (!JwtUserClaimsHelper.TryGetUsuarioId(User, out var usuarioId))
        {
            return Problem(
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID para identificar al usuario.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Usuario no identificado");
        }

        var rolUsuario = JwtUserClaimsHelper.ObtenerRolUsuario(User);
        var usuarioNombre = JwtUserClaimsHelper.ObtenerNombreUsuario(User);

        try
        {
            return await accion(usuarioId, usuarioNombre, rolUsuario);
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
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflicto al despachar la orden de compra");
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
