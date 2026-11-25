using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Recepcion.Api.Models;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Queries;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Domain.Recepciones;

namespace ObraSync.Recepcion.Api.Controllers;

/// <summary>
/// Controlador enfocado en las operaciones de recepción de materiales: registro de recepciones,
/// entregas con problema, listado filtrado, detalle por identificador e historial por orden de compra (SRP / DIP).
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RecepcionesController : RecepcionControllerBase
{
    private readonly ICommandHandler<CrearRecepcionCommand, RecepcionDto> _crearHandler;
    private readonly IQueryHandler<ObtenerRecepcionesQuery, PaginaDe<RecepcionDto>> _obtenerRecepcionesHandler;
    private readonly IQueryHandler<ObtenerRecepcionPorIdQuery, RecepcionDto?> _obtenerPorIdHandler;
    private readonly IQueryHandler<ObtenerRecepcionesPorOrdenQuery, RecepcionesDeOrdenDto?> _obtenerPorOrdenHandler;

    public RecepcionesController(
        ICommandHandler<CrearRecepcionCommand, RecepcionDto> crearHandler,
        IQueryHandler<ObtenerRecepcionesQuery, PaginaDe<RecepcionDto>> obtenerRecepcionesHandler,
        IQueryHandler<ObtenerRecepcionPorIdQuery, RecepcionDto?> obtenerPorIdHandler,
        IQueryHandler<ObtenerRecepcionesPorOrdenQuery, RecepcionesDeOrdenDto?> obtenerPorOrdenHandler,
        IObraAsignacionValidator? obraAsignacionValidator = null)
        : base(obraAsignacionValidator)
    {
        _crearHandler = crearHandler ?? throw new ArgumentNullException(nameof(crearHandler));
        _obtenerRecepcionesHandler = obtenerRecepcionesHandler ?? throw new ArgumentNullException(nameof(obtenerRecepcionesHandler));
        _obtenerPorIdHandler = obtenerPorIdHandler ?? throw new ArgumentNullException(nameof(obtenerPorIdHandler));
        _obtenerPorOrdenHandler = obtenerPorOrdenHandler ?? throw new ArgumentNullException(nameof(obtenerPorOrdenHandler));
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
        if (!TryValidarPaginacion(pagina, tamano, out var p, out var errorPaginacion))
        {
            return errorPaginacion!;
        }

        var correlationId = GetCorrelationIdFromHeader();
        var (obrasAsignadas, errorObra) = await ResolverFiltroObrasCapatazAsync(obraId, correlationId, cancellationToken);
        if (errorObra is not null)
        {
            return errorObra;
        }

        var query = new ObtenerRecepcionesQuery(obraId, ordenCompraId, obrasAsignadas, p);
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
                statusCode: StatusCodes.Status404NotFound,
                detail: "La recepción no existe.");
        }

        var errorObra = await ValidarObraAsignadaACapatazAsync(
            recepcion.ObraId,
            GetCorrelationIdFromHeader(),
            cancellationToken,
            "No se pudo verificar que la obra de la recepción esté asignada al capataz porque el servicio Usuarios y Obras no respondió. Reintentá en unos segundos.");

        if (errorObra is not null)
        {
            return errorObra;
        }

        return Ok(recepcion);
    }

    /// <summary>
    /// Registra una recepción de materiales contra una orden de compra (#189: valida pertenencia de obra para Capataz).
    /// </summary>
    [Authorize(Roles = "Capataz,Admin")]
    [HttpPost]
    public async Task<IActionResult> CrearRecepcion(
        [FromBody] CrearRecepcionRequest request,
        CancellationToken cancellationToken)
    {
        // El usuario se obtiene del token, nunca del cuerpo enviado.
        if (!TryObtenerUsuarioId(out Guid usuarioId))
        {
            return Unauthorized();
        }

        var correlationId = GetCorrelationIdFromHeader();

        // #189: El Capataz solo puede registrar recepciones de obras que tiene asignadas
        var errorObra = await ValidarObraAsignadaACapatazAsync(request.ObraId, correlationId, cancellationToken);
        if (errorObra is not null)
        {
            return errorObra;
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
        if (!TryObtenerUsuarioId(out Guid usuarioId))
        {
            return Unauthorized();
        }

        var correlationId = GetCorrelationIdFromHeader();

        // #189: El Capataz solo puede registrar problemas en obras asignadas
        var errorObra = await ValidarObraAsignadaACapatazAsync(request.ObraId, correlationId, cancellationToken);
        if (errorObra is not null)
        {
            return errorObra;
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
        var resultado = await _obtenerPorOrdenHandler.Handle(
            new ObtenerRecepcionesPorOrdenQuery(ordenCompraId),
            cancellationToken);

        if (resultado is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "La orden de compra no existe.");
        }

        if (EsCapatazSinPrivilegiosSuperiores())
        {
            var errorObra = await ValidarObraAsignadaACapatazAsync(
                resultado.ObraId,
                GetCorrelationIdFromHeader(),
                cancellationToken);

            if (errorObra is not null)
            {
                return errorObra;
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
}
