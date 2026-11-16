using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;

namespace ObraSync.Recepcion.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RecepcionesController : ControllerBase
{
    private readonly CrearRecepcionCommandHandler _crearHandler;
    private readonly ObtenerRecepcionesQueryHandler _obtenerRecepcionesHandler;
    private readonly ObtenerRecepcionPorIdQueryHandler _obtenerPorIdHandler;
    private readonly ObtenerOrdenesPendientesQueryHandler _obtenerOrdenesPendientesHandler;
    private readonly IObraAsignacionValidator? _obraAsignacionValidator;

    [ActivatorUtilitiesConstructor]
    public RecepcionesController(
        CrearRecepcionCommandHandler crearHandler,
        ObtenerRecepcionesQueryHandler obtenerRecepcionesHandler,
        ObtenerRecepcionPorIdQueryHandler obtenerPorIdHandler,
        ObtenerOrdenesPendientesQueryHandler obtenerOrdenesPendientesHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
    {
        _crearHandler = crearHandler;
        _obtenerRecepcionesHandler = obtenerRecepcionesHandler;
        _obtenerPorIdHandler = obtenerPorIdHandler;
        _obtenerOrdenesPendientesHandler = obtenerOrdenesPendientesHandler;
        _obraAsignacionValidator = obraAsignacionValidator;
    }

    public RecepcionesController(
        IRecepcionDbContext context,
        ILogger<RecepcionesController> logger,
        IRecepcionEventPublisher eventPublisher,
        IObraAsignacionValidator? obraAsignacionValidator = null)
        : this(
            new CrearRecepcionCommandHandler(
                context,
                eventPublisher,
                NullLogger<CrearRecepcionCommandHandler>.Instance),
            new ObtenerRecepcionesQueryHandler(context),
            new ObtenerRecepcionPorIdQueryHandler(context),
            new ObtenerOrdenesPendientesQueryHandler(context),
            obraAsignacionValidator)
    {
    }

    /// <summary>
    /// Obtiene las recepciones registradas, opcionalmente filtradas por ObraId y/o OrdenCompraId.
    /// Ambos filtros son opcionales y se pueden combinar.
    /// Roles permitidos: Capataz, Comprador, Admin (#189: Capataz solo puede consultar obras que tiene asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet]
    public async Task<IActionResult> ObtenerRecepciones(
        [FromQuery] Guid? obraId,
        CancellationToken cancellationToken,
        [FromQuery] Guid? ordenCompraId = null)
    {
        var correlationId = GetCorrelationIdFromHeader();

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();

            if (obraId.HasValue)
            {
                bool asignada;
                try
                {
                    asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                        obraId.Value,
                        authHeader,
                        correlationId.ToString(),
                        cancellationToken);
                }
                catch (ObraAsignacionNoVerificableException)
                {
                    return Problem(
                        detail: "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Servicio no disponible");
                }

                if (!asignada)
                {
                    return Problem(
                        detail: "La obra no está asignada al capataz autenticado.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Obra no asignada");
                }
            }
            else
            {
                IReadOnlyList<Guid>? obrasAsignadas;
                try
                {
                    obrasAsignadas = await _obraAsignacionValidator.ObtenerObrasAsignadasAsync(
                        authHeader,
                        correlationId.ToString(),
                        cancellationToken);
                }
                catch (ObraAsignacionNoVerificableException)
                {
                    return Problem(
                        detail: "No se pudo verificar las obras asignadas al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Servicio no disponible");
                }

                var queryAsignadas = new ObtenerRecepcionesQuery(obraId, ordenCompraId, obrasAsignadas);
                var resultadoAsignadas = await _obtenerRecepcionesHandler.Handle(queryAsignadas, cancellationToken);
                return Ok(resultadoAsignadas);
            }
        }

        var query = new ObtenerRecepcionesQuery(obraId, ordenCompraId);
        var resultado = await _obtenerRecepcionesHandler.Handle(query, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Obtiene una recepción por su identificador.
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> ObtenerRecepcionPorId(
        Guid id,
        CancellationToken cancellationToken)
    {
        var query = new ObtenerRecepcionPorIdQuery(id);
        var recepcion = await _obtenerPorIdHandler.Handle(query, cancellationToken);

        if (recepcion == null)
        {
            return Problem(
                statusCode: 404,
                detail: "La recepción no existe.");
        }

        return Ok(recepcion);
    }

    /// <summary>
    /// Obtiene las órdenes de compra pendientes de recibir (sin recepción completa),
    /// opcionalmente filtradas por ObraId y/o por estado SinPartidaPresupuestaria (#196).
    /// Roles permitidos: Capataz, Comprador, Admin (#189: Capataz solo puede consultar órdenes de sus obras asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet("ordenes-pendientes")]
    public async Task<IActionResult> ObtenerOrdenesPendientes(
        [FromQuery] Guid? obraId,
        [FromQuery] bool? sinPartida = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = GetCorrelationIdFromHeader();

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();

            if (obraId.HasValue)
            {
                bool asignada;
                try
                {
                    asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                        obraId.Value,
                        authHeader,
                        correlationId.ToString(),
                        cancellationToken);
                }
                catch (ObraAsignacionNoVerificableException)
                {
                    return Problem(
                        detail: "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Servicio no disponible");
                }

                if (!asignada)
                {
                    return Problem(
                        detail: "La obra no está asignada al capataz autenticado.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Obra no asignada");
                }
            }
            else
            {
                IReadOnlyList<Guid>? obrasAsignadas;
                try
                {
                    obrasAsignadas = await _obraAsignacionValidator.ObtenerObrasAsignadasAsync(
                        authHeader,
                        correlationId.ToString(),
                        cancellationToken);
                }
                catch (ObraAsignacionNoVerificableException)
                {
                    return Problem(
                        detail: "No se pudo verificar las obras asignadas al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Servicio no disponible");
                }

                var queryAsignadas = new ObtenerOrdenesPendientesQuery(obraId, sinPartida, obrasAsignadas);
                var resultadoAsignadas = await _obtenerOrdenesPendientesHandler.Handle(queryAsignadas, cancellationToken);
                return Ok(resultadoAsignadas);
            }
        }

        var query = new ObtenerOrdenesPendientesQuery(obraId, sinPartida);
        var resultado = await _obtenerOrdenesPendientesHandler.Handle(query, cancellationToken);
        return Ok(resultado);
    }

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad binaria y tests unitarios directos sin parámetro sinPartida.
    /// </summary>
    [NonAction]
    public Task<IActionResult> ObtenerOrdenesPendientes(
        Guid? obraId,
        CancellationToken cancellationToken) =>
        ObtenerOrdenesPendientes(obraId, null, cancellationToken);

    /// <summary>
    /// Registra una recepción total de materiales contra una orden de compra (#189: valida pertenencia de obra para Capataz).
    /// </summary>
    [Authorize(Roles = "Capataz,Admin")]
    [HttpPost]
    public async Task<IActionResult> CrearRecepcion(
        [FromBody] CrearRecepcionRequest request,
        CancellationToken cancellationToken)
    {
        // El usuario se obtiene del token, nunca del cuerpo enviado.
        string? usuarioIdTexto = User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(usuarioIdTexto, out Guid usuarioId) ||
            usuarioId == Guid.Empty)
        {
            return Unauthorized();
        }

        var correlationId = GetCorrelationIdFromHeader();

        // #189: El Capataz solo puede registrar recepciones de obras que tiene asignadas
        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();
            bool asignada;
            try
            {
                asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    request.ObraId,
                    authHeader,
                    correlationId.ToString(),
                    cancellationToken);
            }
            catch (ObraAsignacionNoVerificableException)
            {
                return Problem(
                    detail: "No se pudo verificar que la obra esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Servicio no disponible");
            }

            if (!asignada)
            {
                return Problem(
                    detail: "La obra no está asignada al capataz autenticado.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Obra no asignada");
            }
        }

        try
        {
            var command = new CrearRecepcionCommand(
                request.ObraId,
                request.OrdenCompraId,
                usuarioId,
                request.NumeroRemito,
                request.FechaRecepcion,
                request.Observaciones,
                request.Detalles,
                correlationId);

            var creada = await _crearHandler.Handle(command, cancellationToken);
            return StatusCode(201, creada);
        }
        catch (RecepcionConflictoException ex)
        {
            return Problem(statusCode: 409, detail: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: 400, detail: ex.Message);
        }
    }

    private bool EsCapatazSinPrivilegiosSuperiores()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return false;

        return User.IsInRole("Capataz") && !User.IsInRole("Admin") && !User.IsInRole("Comprador");
    }

    private Guid GetCorrelationIdFromHeader()
    {
        var header = Request?.Headers["X-Correlation-ID"].ToString();
        return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }
}
