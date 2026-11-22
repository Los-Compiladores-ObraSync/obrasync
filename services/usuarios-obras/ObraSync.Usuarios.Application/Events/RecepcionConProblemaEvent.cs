namespace ObraSync.Contracts.Events;

/// <summary>
/// Copia local del contrato contracts/events/RecepcionConProblemaEvent.json para el consumidor de Usuarios (#227).
/// </summary>
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
