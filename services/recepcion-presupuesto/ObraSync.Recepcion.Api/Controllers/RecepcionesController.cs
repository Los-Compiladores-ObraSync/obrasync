
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Api.DTOs;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Enums;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;
using DetalleRecepcionEntity = ObraSync.Recepcion.Domain.Entities.DetalleRecepcion;

namespace ObraSync.Recepcion.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class RecepcionesController : ControllerBase
{
    private readonly IRecepcionDbContext _context;
    private readonly ILogger<RecepcionesController> _logger;
    private readonly IRecepcionEventPublisher _eventPublisher;

    public RecepcionesController(
        IRecepcionDbContext context,
        ILogger<RecepcionesController> logger,
        IRecepcionEventPublisher eventPublisher)
    {
        _context = context;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// Obtiene las recepciones registradas, opcionalmente filtradas por ObraId y/o OrdenCompraId.
    /// Ambos filtros son opcionales y se pueden combinar.
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet]
    public async Task<IActionResult> ObtenerRecepciones(
        [FromQuery] Guid? obraId,
        CancellationToken cancellationToken,
        [FromQuery] Guid? ordenCompraId = null)
    {
        var query = _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking();

        if (obraId.HasValue)
        {
            query = query.Where(r => r.ObraId == obraId.Value);
        }

        if (ordenCompraId.HasValue)
        {
            query = query.Where(r => r.OrdenCompraId == ordenCompraId.Value);
        }

        var recepciones = await query.ToListAsync(cancellationToken);
        var resultado = recepciones.Select(RecepcionDto.DesdeEntidad).ToList();
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
        var recepcion = await _context.Recepciones
            .Include(r => r.Detalles)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (recepcion == null)
        {
            return Problem(
                statusCode: 404,
                detail: "La recepción no existe.");
        }

        return Ok(RecepcionDto.DesdeEntidad(recepcion));
    }

    /// <summary>
    /// Obtiene las órdenes de compra pendientes de recibir (sin recepción completa),
    /// opcionalmente filtradas por ObraId.
    /// </summary>
    [Authorize(Roles = "Capataz,Comprador,Admin")]
    [HttpGet("ordenes-pendientes")]
    public async Task<IActionResult> ObtenerOrdenesPendientes(
        [FromQuery] Guid? obraId,
        CancellationToken cancellationToken)
    {
        var query = _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .Where(o => !_context.Recepciones.Any(
                r => r.OrdenCompraId == o.Id &&
                     r.Estado == EstadoRecepcion.Completa));

        if (obraId.HasValue)
        {
            query = query.Where(o => o.ObraId == obraId.Value);
        }

        var ordenes = await query.ToListAsync(cancellationToken);

        // Una sola consulta adicional con lo ya recibido de las órdenes obtenidas.
        var ordenesIds = ordenes.Select(o => o.Id).ToList();

        var recibidos = await _context.Recepciones
            .AsNoTracking()
            .Where(r => ordenesIds.Contains(r.OrdenCompraId))
            .SelectMany(r => r.Detalles.Select(d => new
            {
                r.OrdenCompraId,
                d.ArticuloId,
                d.CantidadRecibida
            }))
            .ToListAsync(cancellationToken);

        var resultado = ordenes
            .Select(o => new OrdenPendienteDto
            {
                OrdenCompraId = o.Id,
                ObraId = o.ObraId,
                Items = o.Items
                    .Select(i => new ItemOrdenPendienteDto
                    {
                        ArticuloId = i.ArticuloId,
                        DescripcionArticulo = i.Descripcion ?? string.Empty,
                        CantidadEsperada = i.Cantidad,
                        CantidadRecibida = recibidos
                            .Where(d => d.OrdenCompraId == o.Id && d.ArticuloId == i.ArticuloId)
                            .Sum(d => d.CantidadRecibida),
                        UnidadMedida = i.UnidadMedida
                    })
                    .ToList()
            })
            .ToList();

        return Ok(resultado);
    }
    /// <summary>
    /// Registra una recepción total de materiales contra una orden de compra.
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

        // Validaciones de los datos generales.
        if (request.ObraId == Guid.Empty ||
            request.OrdenCompraId == Guid.Empty)
        {
            return Problem(
                statusCode: 400,
                detail: "La obra y la orden de compra son obligatorias.");
        }

        if (string.IsNullOrWhiteSpace(request.NumeroRemito) ||
            request.NumeroRemito.Length > 50)
        {
            return Problem(
                statusCode: 400,
                detail: "El número de remito es obligatorio y no puede superar los 50 caracteres.");
        }

        if (request.FechaRecepcion == default)
        {
            return Problem(
                statusCode: 400,
                detail: "La fecha de recepción es obligatoria.");
        }

        // Se permite un dia de tolerancia por la diferencia horaria con UTC.
        DateTime fechaMaximaPermitida = DateTime.UtcNow.Date.AddDays(1);

        if (request.FechaRecepcion.Date > fechaMaximaPermitida)
        {
            return Problem(
                statusCode: 400,
                detail: "La fecha de recepcion no puede ser futura.");
        }

        if (request.Observaciones?.Length > 500)
        {
            return Problem(
                statusCode: 400,
                detail: "Las observaciones no pueden superar los 500 caracteres.");
        }

        if (request.Detalles == null || request.Detalles.Count == 0)
        {
            return Problem(
                statusCode: 400,
                detail: "La recepción debe incluir al menos un material.");
        }

        // Buscar la orden de compra y sus materiales.
        var ordenCompra = await _context.OrdenesCompra
            .Include(o => o.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                o => o.Id == request.OrdenCompraId,
                cancellationToken);

        if (ordenCompra == null)
        {
            return Problem(
                statusCode: 400,
                detail: "La orden de compra no existe.");
        }

        if (ordenCompra.ObraId != request.ObraId)
        {
            return Problem(
                statusCode: 400,
                detail: "La orden de compra no pertenece a la obra indicada.");
        }

        // Una orden completamente recibida no puede registrarse de nuevo.
        bool yaRecibida = await _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == request.OrdenCompraId &&
                     r.Estado == EstadoRecepcion.Completa,
                cancellationToken);

        if (yaRecibida)
        {
            return Problem(
                statusCode: 409,
                detail: "La orden de compra ya fue recibida por completo.");
        }

        // No repetir un número de remito dentro de la misma OC.
        string numeroRemito = request.NumeroRemito.Trim();

        bool remitoRepetido = await _context.Recepciones
            .AnyAsync(
                r => r.OrdenCompraId == request.OrdenCompraId &&
                     r.NumeroRemito == numeroRemito,
                cancellationToken);

        if (remitoRepetido)
        {
            return Problem(
                statusCode: 409,
                detail: "Ya existe una recepción con ese número de remito para esta orden de compra.");
        }

        // Deben recibirse todos los artículos de la OC.
        if (request.Detalles.Count != ordenCompra.Items.Count)
        {
            return Problem(
                statusCode: 400,
                detail: "La recepción debe incluir todos los materiales de la orden de compra.");
        }

        var articulosRepetidos = request.Detalles
            .GroupBy(d => d.ArticuloId)
            .Any(grupo => grupo.Count() > 1);

        if (articulosRepetidos)
        {
            return Problem(
                statusCode: 400,
                detail: "No se puede incluir un mismo artículo más de una vez.");
        }

        var recepcion = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = request.ObraId,
            OrdenCompraId = request.OrdenCompraId,
            RecepcionadoPorUsuarioId = usuarioId,
            NumeroRemito = numeroRemito,
            FechaRecepcion = request.FechaRecepcion,
            Observaciones = request.Observaciones,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var detalleRequest in request.Detalles)
        {
            var articuloComprado = ordenCompra.Items
                .FirstOrDefault(i => i.ArticuloId == detalleRequest.ArticuloId);

            if (articuloComprado == null)
            {
                return Problem(
                    statusCode: 400,
                    detail: "Uno de los artículos no pertenece a la orden de compra.");
            }

            if (detalleRequest.CantidadEsperada != articuloComprado.Cantidad)
            {
                return Problem(
                    statusCode: 400,
                    detail: "La cantidad esperada no coincide con la orden de compra.");
            }

            if (detalleRequest.CantidadRecibida != articuloComprado.Cantidad)
            {
                return Problem(
                    statusCode: 400,
                    detail: "En este sprint solo se permiten recepciones totales. Las recepciones parciales se implementarán en la tarjeta #90.");
            }

            if (string.IsNullOrWhiteSpace(detalleRequest.DescripcionArticulo) ||
                detalleRequest.DescripcionArticulo.Length > 250 ||
                string.IsNullOrWhiteSpace(detalleRequest.UnidadMedida) ||
                detalleRequest.UnidadMedida.Length > 20 ||
                detalleRequest.Observacion?.Length > 500)
            {
                return Problem(
                    statusCode: 400,
                    detail: "Los datos de uno de los materiales no son válidos.");
            }

            var detalle = new DetalleRecepcionEntity
            {
                Id = Guid.NewGuid(),
                ArticuloId = detalleRequest.ArticuloId,
                DescripcionArticulo = detalleRequest.DescripcionArticulo,
                CantidadEsperada = articuloComprado.Cantidad,
                CantidadRecibida = detalleRequest.CantidadRecibida,
                UnidadMedida = detalleRequest.UnidadMedida,
                Conforme = detalleRequest.Conforme,
                Observacion = detalleRequest.Observacion,
                CreatedAt = DateTime.UtcNow
            };

            recepcion.Detalles.Add(detalle);
        }

        // Reutilizar las reglas de dominio de la tarjeta #23.
        EstadoRecepcion estado = recepcion.CalcularEstadoSegunCantidades();
        recepcion.CambiarEstado(estado);

        // Guardar la recepción y sus detalles.
        _context.Recepciones.Add(recepcion);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx &&
                  (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            return Problem(
                statusCode: 409,
                detail: "La orden de compra ya fue recibida o el número de remito está repetido.");
        }

        // Publicar después de guardar. Si falla, la recepción queda guardada (riesgo conocido, sin outbox).
        var correlationId = GetCorrelationIdFromHeader();
        try
        {
            await _eventPublisher.PublishRecepcionConfirmadaAsync(recepcion, correlationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo publicar RecepcionConfirmadaEvent. RecepcionId: {RecepcionId}, CorrelationId: {CorrelationId}. La recepción quedó guardada.",
                recepcion.Id, correlationId);
        }

        return StatusCode(201, RecepcionDto.DesdeEntidad(recepcion));
    }

    private Guid GetCorrelationIdFromHeader()
    {
        var header = Request.Headers["X-Correlation-ID"].ToString();
        return Guid.TryParse(header, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : Guid.NewGuid();
    }
}
