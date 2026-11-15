using System;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Queries
{
    public record GetRequisicionesQuery(
        Guid? ObraId = null,
        string? Estado = null,
        Guid? SolicitanteId = null,
        ParametrosPagina? Paginacion = null
    );
}
