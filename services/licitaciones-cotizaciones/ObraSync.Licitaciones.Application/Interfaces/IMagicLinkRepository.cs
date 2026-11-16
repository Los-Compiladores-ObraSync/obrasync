using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface IMagicLinkRepository
{
    Task AddAsync(MagicLink magicLink, CancellationToken cancellationToken = default);
    Task<MagicLink?> GetByHashAsync(string hash, CancellationToken cancellationToken = default);
}