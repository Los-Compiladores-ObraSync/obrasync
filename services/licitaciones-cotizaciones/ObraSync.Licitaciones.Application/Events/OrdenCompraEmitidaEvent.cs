namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de integración publicado al emitir una Orden de Compra (consumido por Recepción y Control Presupuestario).
/// Cumple el esquema publicado en contracts/events/OrdenCompraEmitidaEvent.json.
/// Es una copia propia del servicio (igual que RequisicionAprobadaEvent): el contexto de build de Docker es solo esta carpeta
/// y no incluye la carpeta contracts de la raíz del repo (tarjeta #132). Si cambia el esquema, hay que cambiar esta copia.
/// </summary>
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
