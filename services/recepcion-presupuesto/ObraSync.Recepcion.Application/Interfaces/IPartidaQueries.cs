using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Partidas.Queries;

namespace ObraSync.Recepcion.Application.Interfaces;

/// <summary>
/// Puerto de consultas de solo lectura para partidas presupuestarias (CQRS / ISP / DIP).
/// </summary>
public interface IPartidaQueries
{
    Task<PaginaDe<PartidaPresupuestariaDto>> ObtenerPartidasAsync(ObtenerPartidasQuery query, CancellationToken cancellationToken = default);
    Task<PartidaPresupuestariaDto?> ObtenerPartidaPorIdAsync(Guid partidaId, CancellationToken cancellationToken = default);
}
