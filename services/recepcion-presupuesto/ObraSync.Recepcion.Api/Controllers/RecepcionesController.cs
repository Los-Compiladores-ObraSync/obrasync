using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Recepcion.Api.Models;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Recepciones;

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
    private readonly ObtenerOrdenesBloqueadasQueryHandler? _obtenerOrdenesBloqueadasHandler;
    private readonly LiberarOrdenCompraCommandHandler? _liberarOrdenHandler;
    private readonly LiquidarOrdenCompraCommandHandler? _liquidarOrdenHandler;
    private readonly ObtenerRecepcionesPorOrdenQueryHandler? _obtenerPorOrdenHandler;
    private readonly ObtenerFotoRecepcionQueryHandler? _obtenerFotoHandler;
    private readonly IObraAsignacionValidator? _obraAsignacionValidator;

    [ActivatorUtilitiesConstructor]
    public RecepcionesController(
        CrearRecepcionCommandHandler crearHandler,
        ObtenerRecepcionesQueryHandler obtenerRecepcionesHandler,
        ObtenerRecepcionPorIdQueryHandler obtenerPorIdHandler,
        ObtenerOrdenesPendientesQueryHandler obtenerOrdenesPendientesHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null,
        LiberarOrdenCompraCommandHandler? liberarOrdenHandler = null,
        ObtenerOrdenesBloqueadasQueryHandler? obtenerOrdenesBloqueadasHandler = null,
        LiquidarOrdenCompraCommandHandler? liquidarOrdenHandler = null,
        ObtenerRecepcionesPorOrdenQueryHandler? obtenerPorOrdenHandler = null,
        ObtenerFotoRecepcionQueryHandler? obtenerFotoHandler = null)
    {
        _crearHandler = crearHandler;
        _obtenerRecepcionesHandler = obtenerRecepcionesHandler;
        _obtenerPorIdHandler = obtenerPorIdHandler;
        _obtenerOrdenesPendientesHandler = obtenerOrdenesPendientesHandler;
        _obraAsignacionValidator = obraAsignacionValidator;
        _liberarOrdenHandler = liberarOrdenHandler;
        _obtenerOrdenesBloqueadasHandler = obtenerOrdenesBloqueadasHandler;
        _liquidarOrdenHandler = liquidarOrdenHandler;
        _obtenerPorOrdenHandler = obtenerPorOrdenHandler;
        _obtenerFotoHandler = obtenerFotoHandler;
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
            obraAsignacionValidator,
            new LiberarOrdenCompraCommandHandler(context),
            new ObtenerOrdenesBloqueadasQueryHandler(context),
            new LiquidarOrdenCompraCommandHandler(context),
            new ObtenerRecepcionesPorOrdenQueryHandler(context),
            new ObtenerFotoRecepcionQueryHandler(context))
    {
    }

    /// <summary>
    /// Obtiene las recepciones registradas, opcionalmente filtradas por ObraId y/o OrdenCompraId.
    /// Ambos filtros son opcionales y se pueden combinar.
    /// Soporta paginación con pagina y tamano (#185).
    /// Roles permitidos: Capataz, Comprador, Admin (#189: Capataz solo puede consultar obras que tiene asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet]
    public async Task<IActionResult> ObtenerRecepciones(
        [FromQuery] Guid? obraId = null,
        CancellationToken cancellationToken = default,
        [FromQuery] Guid? ordenCompraId = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo)
    {
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

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

                var queryAsignadas = new ObtenerRecepcionesQuery(obraId, ordenCompraId, obrasAsignadas, p);
                var resultadoAsignadas = await _obtenerRecepcionesHandler.Handle(queryAsignadas, cancellationToken);
                SetPaginationHeaders(resultadoAsignadas.Total, p);
                return Ok(resultadoAsignadas.Items.ToList());
            }
        }

        var query = new ObtenerRecepcionesQuery(obraId, ordenCompraId, null, p);
        var resultado = await _obtenerRecepcionesHandler.Handle(query, cancellationToken);
        SetPaginationHeaders(resultado.Total, p);
        return Ok(resultado.Items.ToList());
    }

    /// <summary>
    /// Obtiene una recepción por su identificador (#189: valida pertenencia de obra para Capataz).
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

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();
            var correlationId = GetCorrelationIdFromHeader();
            bool asignada;
            try
            {
                asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    recepcion.ObraId,
                    authHeader,
                    correlationId.ToString(),
                    cancellationToken);
            }
            catch (ObraAsignacionNoVerificableException)
            {
                return Problem(
                    detail: "No se pudo verificar que la obra de la recepción esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.",
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

        return Ok(recepcion);
    }

    /// <summary>
    /// Obtiene las órdenes de compra pendientes de recibir (sin recepción completa),
    /// opcionalmente filtradas por ObraId y/o por estado SinPartidaPresupuestaria (#196).
    /// Soporta paginación con pagina y tamano (#185).
    /// Roles permitidos: Capataz, Comprador, Admin (#189: Capataz solo puede consultar órdenes de sus obras asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet("ordenes-pendientes")]
    public async Task<IActionResult> ObtenerOrdenesPendientes(
        [FromQuery] Guid? obraId = null,
        [FromQuery] bool? sinPartida = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

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

                var queryAsignadas = new ObtenerOrdenesPendientesQuery(obraId, sinPartida, obrasAsignadas, p);
                var resultadoAsignadas = await _obtenerOrdenesPendientesHandler.Handle(queryAsignadas, cancellationToken);
                SetPaginationHeaders(resultadoAsignadas.Total, p);
                return Ok(resultadoAsignadas.Items.ToList());
            }
        }

        var query = new ObtenerOrdenesPendientesQuery(obraId, sinPartida, null, p);
        var resultado = await _obtenerOrdenesPendientesHandler.Handle(query, cancellationToken);
        SetPaginationHeaders(resultado.Total, p);
        return Ok(resultado.Items.ToList());
    }

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad binaria y tests unitarios directos sin parámetro sinPartida o paginación.
    /// </summary>
    [NonAction]
    public Task<IActionResult> ObtenerOrdenesPendientes(
        Guid? obraId,
        CancellationToken cancellationToken) =>
        ObtenerOrdenesPendientes(obraId, null, 1, ParametrosPagina.TamanoMaximo, cancellationToken);

    /// <summary>
    /// Sobrecarga auxiliar para compatibilidad binaria y tests unitarios directos con sinPartida sin paginación.
    /// </summary>
    [NonAction]
    public Task<IActionResult> ObtenerOrdenesPendientes(
        Guid? obraId,
        bool? sinPartida,
        CancellationToken cancellationToken) =>
        ObtenerOrdenesPendientes(obraId, sinPartida, 1, ParametrosPagina.TamanoMaximo, cancellationToken);

    /// <summary>
    /// Obtiene las órdenes de compra actualmente bloqueadas por desvío presupuestario (#210).
    /// Soporta filtro opcional por ObraId y paginación con pagina y tamano (#185).
    /// Roles permitidos: Comprador, Admin.
    /// </summary>
    [Authorize(Roles = "Comprador,Admin")]
    [HttpGet("ordenes-bloqueadas")]
    public async Task<IActionResult> ObtenerOrdenesBloqueadas(
        [FromQuery] Guid? obraId = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamano = ParametrosPagina.TamanoMaximo,
        CancellationToken cancellationToken = default)
    {
        if (_obtenerOrdenesBloqueadasHandler is null)
        {
            throw new InvalidOperationException("ObtenerOrdenesBloqueadasQueryHandler no está configurado.");
        }

        var p = new ParametrosPagina(pagina, tamano);
        var error = p.Validar();
        if (error is not null)
        {
            return Problem(detail: error, statusCode: StatusCodes.Status400BadRequest, title: "Paginación inválida");
        }

        var resultado = await _obtenerOrdenesBloqueadasHandler.Handle(
            new ObtenerOrdenesBloqueadasQuery(obraId, p),
            cancellationToken);

        SetPaginationHeaders(resultado.Total, p);
        return Ok(resultado.Items.ToList());
    }

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
                correlationId,
                request.Estado,
                request.MotivoProblema);

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

    /// <summary>
    /// Registra una entrega con problema en estado Rechazada (4) permitiendo adjuntar hasta 3 fotos de evidencia (#217).
    /// No modifica acumulados, remanente ni presupuesto; publica RecepcionConProblemaEvent con la cantidad de fotos.
    /// Roles permitidos: Capataz y Admin (#189: Capataz solo para sus obras asignadas).
    /// </summary>
    [Authorize(Roles = "Capataz,Admin")]
    [HttpPost("problemas")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ValidadorFotoEvidencia.MaxTamanoSolicitudMultipartBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ValidadorFotoEvidencia.MaxTamanoSolicitudMultipartBytes)]
    public async Task<IActionResult> RegistrarEntregaConProblema(
        [FromForm] RegistrarEntregaConProblemaFormRequest request,
        CancellationToken cancellationToken = default)
    {
        string? usuarioIdTexto = User.FindFirst("sub")?.Value
                                 ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(usuarioIdTexto, out Guid usuarioId) ||
            usuarioId == Guid.Empty)
        {
            return Unauthorized();
        }

        var correlationId = GetCorrelationIdFromHeader();

        // #189: El Capataz solo puede registrar problemas en obras asignadas
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

        var fotosEntrada = new List<FotoEvidenciaEntrada>();
        if (request.Fotos is { Count: > 0 })
        {
            if (request.Fotos.Count > ValidadorFotoEvidencia.MaxFotosPorRecepcion)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    detail: $"Se permiten como máximo {ValidadorFotoEvidencia.MaxFotosPorRecepcion} fotos por entrega con problema.");
            }

            foreach (var archivo in request.Fotos)
            {
                if (archivo.Length == 0)
                {
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        detail: "La foto de evidencia está vacía.");
                }

                if (archivo.Length > ValidadorFotoEvidencia.MaxTamanoFotoBytes)
                {
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        detail: "Cada foto de evidencia no puede superar los 2 MB.");
                }

                var bytes = await LeerArchivoConTopeAsync(archivo, ValidadorFotoEvidencia.MaxTamanoFotoBytes, cancellationToken);
                if (bytes is null)
                {
                    return Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        detail: "Cada foto de evidencia no puede superar los 2 MB.");
                }

                fotosEntrada.Add(new FotoEvidenciaEntrada(archivo.FileName, bytes));
            }
        }

        try
        {
            var command = new CrearRecepcionCommand(
                request.ObraId,
                request.OrdenCompraId,
                usuarioId,
                request.NumeroRemito ?? string.Empty,
                request.FechaRecepcion ?? DateTime.UtcNow,
                request.Detalle,
                Array.Empty<CrearDetalleRecepcionRequest>(),
                correlationId,
                EstadoRecepcion.Rechazada,
                request.Motivo,
                fotosEntrada);

            var creada = await _crearHandler.Handle(command, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, creada);
        }
        catch (RecepcionConflictoException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, detail: ex.Message);
        }
        catch (ArgumentException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: ex.Message);
        }
    }

    /// <summary>
    /// Obtiene el historial cronológico de recepciones de una orden de compra con estado, motivo y fotos,
    /// junto con los datos de despacho y saldos por material (#217).
    /// Roles permitidos: Capataz (obra asignada), Comprador, Admin y Proveedor (solo dueño de la orden).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin,Proveedor")]
    [HttpGet("ordenes/{ordenCompraId:guid}")]
    public async Task<IActionResult> ObtenerRecepcionesPorOrden(
        [FromRoute] Guid ordenCompraId,
        CancellationToken cancellationToken = default)
    {
        if (_obtenerPorOrdenHandler is null)
        {
            throw new InvalidOperationException("ObtenerRecepcionesPorOrdenQueryHandler no está configurado.");
        }

        var resultado = await _obtenerPorOrdenHandler.Handle(
            new ObtenerRecepcionesPorOrdenQuery(ordenCompraId),
            cancellationToken);

        if (resultado is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La orden de compra no existe.");
        }

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();
            var correlationId = GetCorrelationIdFromHeader();
            bool asignada;
            try
            {
                asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    resultado.ObraId,
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
        else if (EsProveedorSinPrivilegiosSuperiores())
        {
            if (!TryObtenerUsuarioId(out var proveedorUsuarioId))
            {
                return Unauthorized();
            }

            if (resultado.ProveedorId != proveedorUsuarioId)
            {
                return Problem(
                    detail: "La orden de compra no pertenece al proveedor autenticado.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Orden no autorizada");
            }
        }

        return Ok(resultado);
    }

    /// <summary>
    /// Descarga una foto de evidencia asociada a una entrega con problema (#217), con Cache-Control privado.
    /// Roles permitidos: Capataz (obra asignada), Comprador, Admin y Proveedor (solo dueño de la orden).
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin,Proveedor")]
    [HttpGet("{id:guid}/fotos/{fotoId:guid}")]
    public async Task<IActionResult> DescargarFotoRecepcion(
        [FromRoute] Guid id,
        [FromRoute] Guid fotoId,
        CancellationToken cancellationToken = default)
    {
        if (_obtenerFotoHandler is null)
        {
            throw new InvalidOperationException("ObtenerFotoRecepcionQueryHandler no está configurado.");
        }

        var foto = await _obtenerFotoHandler.Handle(
            new ObtenerFotoRecepcionQuery(id, fotoId),
            cancellationToken);

        if (foto is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La foto de evidencia no existe.");
        }

        if (EsCapatazSinPrivilegiosSuperiores() && _obraAsignacionValidator != null)
        {
            var authHeader = Request?.Headers["Authorization"].ToString();
            var correlationId = GetCorrelationIdFromHeader();
            bool asignada;
            try
            {
                asignada = await _obraAsignacionValidator.EstaAsignadaACapatazAsync(
                    foto.ObraId,
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
        else if (EsProveedorSinPrivilegiosSuperiores())
        {
            if (!TryObtenerUsuarioId(out var proveedorUsuarioId))
            {
                return Unauthorized();
            }

            if (!foto.ProveedorId.HasValue || foto.ProveedorId.Value != proveedorUsuarioId)
            {
                return Problem(
                    detail: "No tenés permisos para descargar fotos de esta orden de compra.",
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Acceso denegado");
            }
        }

        if (Response?.Headers is not null)
        {
            Response.Headers["Cache-Control"] = "private, max-age=3600";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
        }

        return File(foto.Contenido, foto.ContentType);
    }

    /// <summary>
    /// Registra la liberación de una orden de compra bloqueada por desvío presupuestario (#96, #154).
    /// Exclusivo para el rol Admin. Registra quién (sub del token) y cuándo la liberó,
    /// y suma su monto a MontoComprometido de la partida correspondiente.
    /// POST /api/recepciones/ordenes/{id}/liberaciones
    /// </summary>
    [Authorize(Roles = "Admin")]
    [HttpPost("ordenes/{id:guid}/liberaciones")]
    [HttpPost("ordenes-compra/{id:guid}/liberaciones")]
    public async Task<IActionResult> LiberarOrdenCompra(
        [FromRoute] Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LiberarOrdenCompraRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (_liberarOrdenHandler is null)
        {
            throw new InvalidOperationException("LiberarOrdenCompraCommandHandler no está configurado.");
        }

        string? usuarioIdTexto = User.FindFirst("sub")?.Value
                                 ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(usuarioIdTexto, out Guid usuarioId) ||
            usuarioId == Guid.Empty)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.");
        }

        var resultado = await _liberarOrdenHandler.Handle(
            new LiberarOrdenCompraCommand(id, usuarioId, request?.Motivo),
            cancellationToken);

        return Ok(resultado);
    }

    /// <summary>
    /// Registra la liquidación administrativa de una orden de compra (#92).
    /// Bloquea la liquidación con 409 Conflict (ProblemDetails) mientras exista saldo pendiente de recepción (criterio CU-06).
    /// Roles permitidos: Comprador y Admin.
    /// POST /api/recepciones/ordenes/{id}/liquidaciones
    /// </summary>
    [Authorize(Roles = "Comprador,Admin")]
    [HttpPost("ordenes/{id:guid}/liquidaciones")]
    [HttpPost("ordenes-compra/{id:guid}/liquidaciones")]
    public async Task<IActionResult> LiquidarOrdenCompra(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (_liquidarOrdenHandler is null)
        {
            throw new InvalidOperationException("LiquidarOrdenCompraCommandHandler no está configurado.");
        }

        string? usuarioIdTexto = User.FindFirst("sub")?.Value
                                 ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(usuarioIdTexto, out Guid usuarioId) ||
            usuarioId == Guid.Empty)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "No autorizado",
                detail: "El token JWT no contiene un claim 'sub' válido con formato GUID.");
        }

        var resultado = await _liquidarOrdenHandler.Handle(
            new LiquidarOrdenCompraCommand(id, usuarioId),
            cancellationToken);

        return Ok(resultado);
    }

    private bool EsCapatazSinPrivilegiosSuperiores()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return false;

        return User.IsInRole("Capataz") && !User.IsInRole("Admin") && !User.IsInRole("Comprador");
    }

    private bool EsProveedorSinPrivilegiosSuperiores()
    {
        if (User?.Identity?.IsAuthenticated != true)
            return false;

        return User.IsInRole("Proveedor") && !User.IsInRole("Admin") && !User.IsInRole("Comprador");
    }

    private bool TryObtenerUsuarioId(out Guid usuarioId)
    {
        string? usuarioIdTexto = User.FindFirst("sub")?.Value
                                 ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(usuarioIdTexto, out usuarioId) && usuarioId != Guid.Empty;
    }

    private static async Task<byte[]?> LeerArchivoConTopeAsync(
        IFormFile archivo,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var stream = archivo.OpenReadStream();
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int totalLeidos = 0;

        while (true)
        {
            int leidos = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (leidos == 0)
            {
                break;
            }

            totalLeidos += leidos;
            if (totalLeidos > maxBytes)
            {
                return null;
            }

            ms.Write(buffer, 0, leidos);
        }

        return ms.ToArray();
    }

    private Guid GetCorrelationIdFromHeader()
    {
        var header = Request?.Headers["X-Correlation-ID"].ToString();
        return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }

    private void SetPaginationHeaders(int total, ParametrosPagina p)
    {
        if (Response?.Headers is not null)
        {
            Response.Headers["X-Total-Count"] = total.ToString();
            Response.Headers["X-Page"] = p.Pagina.ToString();
            Response.Headers["X-Page-Size"] = p.Tamano.ToString();
        }
    }
}
