using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ObraSync.Contracts.Events;

/// <summary>
/// Evento publicado por Recepción y Control Presupuestario cuando se confirma una recepción de materiales.
/// Cumple con contracts/events/RecepcionConfirmadaEvent.json (additionalProperties: false).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record RecepcionConfirmadaEvent(
    Guid EventId,
    DateTime OccurredAt,
    Guid CorrelationId,
    Guid RecepcionId,
    Guid ObraId,
    Guid OrdenCompraId,
    string NumeroRemito,
    DateTime FechaRecepcion,
    IReadOnlyList<RecepcionConfirmadaItemEvent> Items
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record RecepcionConfirmadaItemEvent(
    Guid ArticuloId,
    decimal CantidadRecibida,
    string UnidadMedida,
    string? DescripcionArticulo = null,
    decimal? CantidadEsperada = null,
    bool? Conforme = null,
    string? Observacion = null
);

