using System;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands
{
    public record RechazarRequisicionCommand(
        Guid RequisicionId,
        Guid RechazadorId,
        string Motivo
    );
}
