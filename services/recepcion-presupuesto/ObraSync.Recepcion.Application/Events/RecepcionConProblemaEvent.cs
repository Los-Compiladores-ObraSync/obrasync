using System;
using System.Text.Json.Serialization;

namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento publicado por Recepción y Control Presupuestario cuando se registra una entrega con problema
/// en estado Rechazada (#217). Consumido por Licitaciones (#218) y Usuarios (#227).
/// Cumple con contracts/events/RecepcionConProblemaEvent.json (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record RecepcionConProblemaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid RecepcionId,
    string Motivo,
    string? Detalle,
    int CantidadFotos,
    Guid? ObraId = null,
    Guid? ProveedorId = null
);
