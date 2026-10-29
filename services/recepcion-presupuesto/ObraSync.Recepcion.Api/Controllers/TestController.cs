using MassTransit;
using Microsoft.AspNetCore.Mvc;
using ObraSync.Contracts.Events;

namespace ObraSync.Recepcion.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<TestController> _logger;

    public TestController(IPublishEndpoint publishEndpoint, ILogger<TestController> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    [HttpPost("publish-orden-compra")]
    public async Task<IActionResult> PublishOrdenCompra([FromBody] PublishOrdenCompraRequest request, CancellationToken cancellationToken)
    {
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
            items
        );

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);
            _logger.LogInformation("Evento OrdenCompraEmitida publicado. EventId: {EventId}, OrdenCompraId: {OrdenCompraId}", eventId, request.OrdenCompraId);
            return Ok(new { eventId, message = "Evento publicado correctamente" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar evento OrdenCompraEmitida. EventId: {EventId}", eventId);
            return StatusCode(500, new { error = ex.Message });
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
    public decimal MontoTotal { get; set; }
    public List<PublishOrdenCompraItemRequest> Items { get; set; } = [];
}

public class PublishOrdenCompraItemRequest
{
    public Guid ArticuloId { get; set; }
    public decimal Cantidad { get; set; }
    public required string UnidadMedida { get; set; }
    public decimal PrecioUnitario { get; set; }
    public string? Descripcion { get; set; }
}
