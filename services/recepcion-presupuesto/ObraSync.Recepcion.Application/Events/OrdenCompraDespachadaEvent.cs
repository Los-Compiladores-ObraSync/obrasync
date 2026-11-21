using System;
using System.Text.Json.Serialization;

namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento publicado por el microservicio de Licitaciones y Cotizaciones cuando una Orden de Compra es marcada como despachada (#202, #204).
/// Consumido por Recepción y Control Presupuestario.
/// Cumple estrictamente con contracts/events/OrdenCompraDespachadaEvent.json (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record OrdenCompraDespachadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid ObraId,
    DateTime FechaDespacho,
    DateTime? FechaEstimadaLlegada = null,
    string? ObservacionDespacho = null
);
