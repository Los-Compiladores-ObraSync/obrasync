namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de integración publicado al marcar una Orden de Compra como despachada (consumido por Recepción y Control Presupuestario).
/// Cumple el esquema publicado en contracts/events/OrdenCompraDespachadaEvent.json (#202 / #203).
/// Es una copia propia del servicio porque el contexto de build de Docker no incluye la carpeta contracts de la raíz del repo (#132).
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
