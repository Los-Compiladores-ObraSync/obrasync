using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

/// <summary>
/// Consultas de lectura paginadas directamente en base de datos sobre usuarios y roles (#254).
/// </summary>
public interface IUsuarioQueryRepository
{
    /// <summary>
    /// Obtiene una página de usuarios activos con el rol indicado, ordenados por nombre y desempatados por Id,
    /// junto al total de coincidencias antes de paginar.
    /// </summary>
    Task<(IReadOnlyList<UsuarioResumenDto> Items, int Total)> GetActivosPorRolPaginadosAsync(
        string rol,
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default);
}
