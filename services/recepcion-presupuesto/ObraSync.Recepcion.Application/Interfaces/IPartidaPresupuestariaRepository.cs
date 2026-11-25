using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Application.Interfaces;

public interface IPartidaPresupuestariaRepository
{
    Task<bool> ExisteCodigoPartidaAsync(Guid obraId, string codigoNormalizadoLower, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string?>> ObtenerEtapasPorObraAsync(Guid obraId, CancellationToken cancellationToken = default);
    Task<List<PartidaPresupuestaria>> ObtenerPartidasPorObraParaActualizarAsync(Guid obraId, CancellationToken cancellationToken = default);
    Task AgregarPartidaAsync(PartidaPresupuestaria partida, CancellationToken cancellationToken = default);
}
