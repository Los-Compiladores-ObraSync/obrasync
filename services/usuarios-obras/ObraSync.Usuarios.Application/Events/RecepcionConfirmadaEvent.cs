namespace ObraSync.Contracts.Events;

/// <summary>
/// Copia local del contrato contracts/events/RecepcionConfirmadaEvent.json para el consumidor de Usuarios (#227).
/// Incluye ProveedorId opcional para compatibilidad futura si Recepción lo agrega.
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
    Guid? ProveedorId = null
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
