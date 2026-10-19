using System;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands
{
    public record AprobarRequisicionCommand(
        Guid RequisicionId,
        Guid AprobadorId,
        Guid CorrelationId
    );
}
