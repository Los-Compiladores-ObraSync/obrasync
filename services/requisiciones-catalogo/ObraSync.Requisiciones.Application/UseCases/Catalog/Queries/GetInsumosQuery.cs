namespace ObraSync.Requisiciones.Application.UseCases.Catalog.Queries
{
    public record GetInsumosQuery(
        string? Search = null,
        bool SoloActivos = true
    );
}
