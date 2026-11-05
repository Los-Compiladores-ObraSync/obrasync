namespace ObraSync.Contracts.Events;

public record OrdenCompraEmitidaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid LicitacionId,
    Guid RequisicionId,
    Guid ObraId,
    Guid ProveedorId,
    string? Etapa,
    decimal MontoTotal,
    IReadOnlyCollection<OrdenCompraItemDto> Items
);

public record OrdenCompraItemDto(
    Guid ArticuloId,
    string? Descripcion,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioUnitario
);