namespace ObraSync.Contracts.Events;

/// <summary>
/// Copia local del contrato contracts/events/RequisicionAprobadaEvent.json para el consumidor de Usuarios (#227).
/// El contexto de Docker de cada servicio no incluye la carpeta raíz contracts (#132).
/// </summary>
public record RequisicionAprobadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid RequisicionId,
    string? NumeroRequisicion,
    Guid ObraId,
    Guid SolicitanteId,
    Guid AprobadorId,
    IReadOnlyList<RequisicionAprobadaItemEvent> Items,
    string? Etapa = null
);

public record RequisicionAprobadaItemEvent(
    Guid ArticuloId,
    string? Descripcion,
    decimal Cantidad,
    string UnidadMedida
);
