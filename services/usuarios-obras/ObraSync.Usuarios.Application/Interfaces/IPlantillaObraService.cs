using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

/// <summary>
/// Casos de uso de aplicación para consulta y creación de plantillas de obra.
/// </summary>
public interface IPlantillaObraService
{
    Task<(IReadOnlyList<PlantillaObraResponseDto> Items, int Total)> ObtenerPaginadasAsync(
        ParametrosPagina? paginacion = null,
        CancellationToken cancellationToken = default);

    Task<PlantillaObraResponseDto?> ObtenerPorIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PlantillaObraResponseDto> CrearAsync(
        CrearPlantillaObraRequestDto request,
        CancellationToken cancellationToken = default);
}
