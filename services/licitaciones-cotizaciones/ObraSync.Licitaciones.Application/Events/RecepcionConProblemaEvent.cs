namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de integración publicado por Recepción y Control Presupuestario cuando el Capataz registra una entrega con problema (#217).
/// Cumple con contracts/events/RecepcionConProblemaEvent.json (#217, #218).
/// Es una copia propia del servicio porque el contexto de build de Docker no incluye la carpeta contracts de la raíz del repo (#132).
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
