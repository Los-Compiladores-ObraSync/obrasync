using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Application.Interfaces;

/// <summary>
/// Abstracción de acceso a datos para las plantillas de obra preconfiguradas y personalizadas.
/// </summary>
public interface IPlantillaObraRepository
{
    Task<(IReadOnlyList<PlantillaObra> Items, int Total)> ObtenerPaginadasAsync(
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default);

    Task<PlantillaObra?> ObtenerPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PlantillaObra> CrearAsync(
        PlantillaObra plantilla,
        CancellationToken cancellationToken = default);
}
