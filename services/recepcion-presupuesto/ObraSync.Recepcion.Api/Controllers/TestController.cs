using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Common.Interfaces;

namespace ObraSync.Recepcion.Api.Controllers;

// Endpoint solo para pruebas internas (publica un evento de OC inventado). Antes estaba abierto a cualquiera;
// ahora exige un JWT con rol Admin (tarjeta #136) y solo está activo en entornos Development y Testing (tarjeta #175).
[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TestController> _logger;
    private readonly IHostEnvironment? _environment;
    private readonly IUnitOfWork? _unitOfWork;

    public TestController(
        IPublishEndpoint publishEndpoint,
        ILogger<TestController> logger,
        IHostEnvironment? environment = null,
        IUnitOfWork? unitOfWork = null)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _environment = environment;
        _unitOfWork = unitOfWork;
    }

    [HttpPost("ordenes-compra-emitidas")]
    public async Task<IActionResult> PublishOrdenCompra([FromBody] PublishOrdenCompraRequest request, CancellationToken cancellationToken)
    {
        if (_environment != null && !_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        if (request == null || request.Items == null || request.Items.Count == 0)
        {
            _logger.LogWarning("Solicitud de testing inválida: items vacíos");
            return BadRequest(new { error = "Items no puede estar vacío" });
        }

        if (request.MontoTotal < 0 || request.ObraId == Guid.Empty)
        {
            _logger.LogWarning("Solicitud de testing inválida: MontoTotal negativo u ObraId vacío");
            return BadRequest(new { error = "MontoTotal debe ser >= 0 y ObraId no puede estar vacío" });
        }

        var eventId = request.EventId == Guid.Empty ? Guid.NewGuid() : request.EventId;

        var items = request.Items.Select(i => new OrdenCompraEmitidaItemEvent(
            i.ArticuloId,
            i.Cantidad,
            i.UnidadMedida,
            i.PrecioUnitario,
            i.Descripcion
        )).ToList();

        var @event = new OrdenCompraEmitidaEvent(
            eventId,
            DateTime.UtcNow,
            request.CorrelationId ?? eventId,
            request.OrdenCompraId,
            request.LicitacionId ?? Guid.NewGuid(),
            request.RequisicionId ?? Guid.NewGuid(),
            request.ObraId,
            request.ProveedorId,
            request.MontoTotal,
            items,
            request.Etapa
        );

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);

            if (request.MarcarComoDespachada)
            {
                var despachadaEvent = new OrdenCompraDespachadaEvent(
                    EventId: Guid.NewGuid(),
                    OccurredAt: DateTime.UtcNow,
                    CorrelationId: @event.CorrelationId,
                    OrdenCompraId: request.OrdenCompraId,
                    ObraId: request.ObraId,
                    FechaDespacho: DateTime.UtcNow);

                await _publishEndpoint.Publish(despachadaEvent, cancellationToken);
            }

            if (_unitOfWork != null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Evento OrdenCompraEmitida publicado. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}, MarcarComoDespachada: {MarcarComoDespachada}",
                eventId, request.OrdenCompraId, request.MarcarComoDespachada);
            return Ok(new { eventId, message = "Evento publicado correctamente" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar evento OrdenCompraEmitida. EventId: {EventId}", eventId);
            return StatusCode(500, new { error = "No se pudo publicar el evento. Revisá los logs del servicio." });
        }
    }

    [HttpPost("ordenes-compra-despachadas")]
    public async Task<IActionResult> PublishOrdenDespachada(
        [FromBody] PublishOrdenDespachadaRequest request,
        CancellationToken cancellationToken)
    {
        if (_environment != null && !_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            return NotFound();
        }

        if (request == null || request.OrdenCompraId == Guid.Empty || request.ObraId == Guid.Empty)
        {
            _logger.LogWarning("Solicitud de testing inválida: OrdenCompraId u ObraId vacío");
            return BadRequest(new { error = "OrdenCompraId y ObraId son obligatorios" });
        }

        var eventId = request.EventId == Guid.Empty ? Guid.NewGuid() : request.EventId;
        var fechaDespacho = request.FechaDespacho.HasValue
            ? DateTime.SpecifyKind(request.FechaDespacho.Value, DateTimeKind.Utc)
            : DateTime.UtcNow;

        var @event = new OrdenCompraDespachadaEvent(
            EventId: eventId,
            OccurredAt: DateTime.UtcNow,
            CorrelationId: request.CorrelationId ?? eventId,
            OrdenCompraId: request.OrdenCompraId,
            ObraId: request.ObraId,
            FechaDespacho: fechaDespacho);

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);
            if (_unitOfWork != null)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Evento OrdenCompraDespachada publicado. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}",
                eventId, request.OrdenCompraId);
            return Ok(new { eventId, message = "Evento OrdenCompraDespachada publicado correctamente" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar evento OrdenCompraDespachada. EventId: {EventId}", eventId);
            return StatusCode(500, new { error = "No se pudo publicar el evento. Revisá los logs del servicio." });
        }
    }
}

public class PublishOrdenCompraRequest
{
    public Guid EventId { get; set; } = Guid.Empty;
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public Guid ProveedorId { get; set; }
    public Guid? CorrelationId { get; set; }
    public Guid? LicitacionId { get; set; }
    public Guid? RequisicionId { get; set; }
    public string? Etapa { get; set; }
    public decimal MontoTotal { get; set; }
    public bool MarcarComoDespachada { get; set; } = true;
    public List<PublishOrdenCompraItemRequest> Items { get; set; } = [];
}

public class PublishOrdenDespachadaRequest
{
    public Guid EventId { get; set; } = Guid.Empty;
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime? FechaDespacho { get; set; }
}

public class PublishOrdenCompraItemRequest
{
    public Guid ArticuloId { get; set; }
    public decimal Cantidad { get; set; }
    public required string UnidadMedida { get; set; }
    public decimal PrecioUnitario { get; set; }
    public string? Descripcion { get; set; }
}
