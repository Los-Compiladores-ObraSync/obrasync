namespace ObraSync.Contracts.Events;

public record OrdenCompraDespachadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid ObraId,
    DateTime FechaDespacho,
    DateTime? FechaEstimadaLlegada = null,
    string? Observacion = null
);
