using System.Collections.Generic;
using MediatR;
using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.Queries.ListarInsumos
{
    public record ListarInsumosQuery(string? Search = null, bool SoloActivos = true) : IRequest<IReadOnlyList<InsumoCatalogoDto>>;
}
