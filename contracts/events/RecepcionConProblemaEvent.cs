namespace ObraSync.Contracts.Events;

public record RecepcionConProblemaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid RecepcionId,
    string Motivo,
    string? Detalle,
    int CantidadFotos,
    Guid? ObraId = null,
    Guid? ProveedorId = null
);
