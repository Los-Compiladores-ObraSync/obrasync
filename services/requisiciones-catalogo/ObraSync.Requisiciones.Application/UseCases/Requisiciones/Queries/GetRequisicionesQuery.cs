using System;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries
{
    public record GetRequisicionesQuery(
        Guid? ObraId = null,
        string? Estado = null
    );
}
