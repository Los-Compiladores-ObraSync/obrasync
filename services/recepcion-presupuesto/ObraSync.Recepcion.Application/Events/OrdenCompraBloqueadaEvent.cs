using System;
using System.Text.Json.Serialization;

namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento de auditoría publicado por Recepción y Control Presupuestario cuando una Orden de Compra
/// supera la partida presupuestaria asignada a la obra/etapa y queda en estado Bloqueada (#95, #154).
/// Cumple estrictamente con contracts/events/OrdenCompraBloqueadaEvent.json (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record OrdenCompraBloqueadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid OrdenCompraId,
    Guid ObraId,
    Guid ProveedorId,
    Guid PartidaId,
    string? Etapa,
    decimal MontoOrdenCompra,
    decimal MontoAsignadoPartida,
    decimal MontoComprometidoActual,
    decimal MontoProyectado,
    decimal Exceso,
    string Motivo
);
