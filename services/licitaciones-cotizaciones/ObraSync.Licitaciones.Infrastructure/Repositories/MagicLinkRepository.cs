using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class MagicLinkRepository : IMagicLinkRepository
{
    private readonly LicitacionesDbContext _context;

    public MagicLinkRepository(LicitacionesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(MagicLink magicLink, CancellationToken cancellationToken = default)
    {
        await _context.MagicLinks.AddAsync(magicLink, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MagicLink?> GetByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        return await _context.MagicLinks
            .FirstOrDefaultAsync(x => x.Hash == hash, cancellationToken);
    }
}