namespace ObraSync.Contracts.Events;

/// <summary>
/// Copia local del contrato contracts/events/OrdenCompraDespachadaEvent.json para el consumidor de Usuarios (#227).
/// </summary>
public record OrdenCompraDespachadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid ObraId,
    DateTime FechaDespacho,
    DateTime? FechaEstimadaLlegada = null,
    string? ObservacionDespacho = null,
    bool EsRedespacho = false,
    int NumeroDespacho = 1
);
