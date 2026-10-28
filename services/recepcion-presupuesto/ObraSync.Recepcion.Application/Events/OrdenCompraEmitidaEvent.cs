using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento publicado por el microservicio de Licitaciones y Cotizaciones cuando se emite una Orden de Compra.
/// Consumido por Recepción y Control Presupuestario.
/// Cumple estrictamente con contracts/events/OrdenCompraEmitidaEvent.json (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record OrdenCompraEmitidaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid LicitacionId,
    Guid RequisicionId,
    Guid ObraId,
    Guid ProveedorId,
    decimal MontoTotal,
    IReadOnlyList<OrdenCompraEmitidaItemEvent> Items
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record OrdenCompraEmitidaItemEvent(
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal PrecioUnitario,
    string? Descripcion = null
);
