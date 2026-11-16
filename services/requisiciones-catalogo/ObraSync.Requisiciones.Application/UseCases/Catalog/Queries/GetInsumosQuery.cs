using ObraSync.Requisiciones.Application.DTOs;

namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Queries
{
    public record GetInsumosQuery(
        string? Search = null,
        bool SoloActivos = true,
        ParametrosPagina? Paginacion = null
    );
}
