using System;
using System.Collections.Generic;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands
{
    public record CreateRequisicionCommand(
        Guid ObraId,
        Guid SolicitanteId,
        IReadOnlyList<CrearRequisicionItemDto> Items,
        string? Etapa = null,
        string? Observaciones = null,
        string? IdempotencyKey = null
    );
}
