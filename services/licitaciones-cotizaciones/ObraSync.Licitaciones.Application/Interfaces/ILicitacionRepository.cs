using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ILicitacionRepository
{
    Task<Licitacion?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Licitacion>> GetFilteredAsync(
        Guid? obraId = null,
        string? estado = null,
        string? rolUsuario = null,
        CancellationToken cancellationToken = default);
}
