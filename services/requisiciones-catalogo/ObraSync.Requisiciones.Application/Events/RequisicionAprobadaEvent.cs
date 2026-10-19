using System;
using System.Collections.Generic;

namespace ObraSync.Contracts.Events
{
    /// <summary>
    /// Evento de integración publicado cuando una requisición pasa a estado Aprobada.
    /// Cumple el esquema publicado en contracts/events/RequisicionAprobadaEvent.json.
    /// </summary>
    public record RequisicionAprobadaEvent(
        Guid EventId,
        DateTime OccurredAt,
        Guid CorrelationId,
        Guid RequisicionId,
        string NumeroRequisicion,
        Guid ObraId,
        Guid SolicitanteId,
        Guid AprobadorId,
        IReadOnlyList<RequisicionAprobadaItemEvent> Items
    );

    public record RequisicionAprobadaItemEvent(
        Guid ArticuloId,
        string Descripcion,
        decimal Cantidad,
        string UnidadMedida
    );
}
