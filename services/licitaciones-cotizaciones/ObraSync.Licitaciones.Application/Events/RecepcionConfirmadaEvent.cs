namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de integración publicado por Recepción y Control Presupuestario cuando se confirma una recepción de materiales.
/// Cumple con contracts/events/RecepcionConfirmadaEvent.json (#217, #222).
/// Es una copia propia del servicio porque el contexto de build de Docker no incluye la carpeta contracts de la raíz del repo (#132).
/// </summary>
public record RecepcionConfirmadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid RecepcionId,
    Guid ObraId,
    Guid OrdenCompraId,
    string NumeroRemito,
    DateTime FechaRecepcion,
    IReadOnlyList<RecepcionConfirmadaItemEvent> Items,
    bool OrdenCompleta = false,
    string? Estado = null
);

public record RecepcionConfirmadaItemEvent(
    Guid ArticuloId,
    decimal CantidadRecibida,
    string UnidadMedida,
    string? DescripcionArticulo = null,
    decimal? CantidadEsperada = null,
    bool? Conforme = null,
    string? Observacion = null
);
