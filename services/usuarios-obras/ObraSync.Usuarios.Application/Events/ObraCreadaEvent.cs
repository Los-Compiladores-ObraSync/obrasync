namespace ObraSync.Contracts.Events;

public record ObraCreadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid ObraId,
    string Nombre,
    IReadOnlyList<string> Etapas
);
