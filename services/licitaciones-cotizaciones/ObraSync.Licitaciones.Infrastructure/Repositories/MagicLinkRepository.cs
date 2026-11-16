using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.MagicLinks;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class MagicLinkRepository : IMagicLinkRepository
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> LocksPorEnlace = new();
    private readonly LicitacionesDbContext _context;

    public MagicLinkRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task AddAsync(MagicLink magicLink, CancellationToken cancellationToken = default)
    {
        await _context.MagicLinks.AddAsync(magicLink, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MagicLink?> GetByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return null;
        }

        var tokenLimpio = hash.Trim();
        var sha256 = MagicLinkHashHelper.ComputeSha256(tokenLimpio);

        return await _context.MagicLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Hash == sha256 || x.Hash == tokenLimpio,
                cancellationToken);
    }

    public async Task<bool> TryConsumirAtomicoAsync(
        Guid magicLinkId,
        DateTime ahoraUtc,
        CancellationToken cancellationToken = default)
    {
        if (_context.Database.IsRelational())
        {
            var filasAfectadas = await _context.MagicLinks
                .Where(x => x.Id == magicLinkId && x.FechaDeUso == null && x.FechaDeVencimiento > ahoraUtc)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(x => x.FechaDeUso, ahoraUtc),
                    cancellationToken);

            return filasAfectadas == 1;
        }

        var semaforo = LocksPorEnlace.GetOrAdd(magicLinkId, _ => new SemaphoreSlim(1, 1));
        await semaforo.WaitAsync(cancellationToken);
        try
        {
            var tracked = _context.ChangeTracker.Entries<MagicLink>()
                .FirstOrDefault(e => e.Entity.Id == magicLinkId);
            if (tracked != null)
            {
                tracked.State = EntityState.Detached;
            }

            var link = await _context.MagicLinks
                .FirstOrDefaultAsync(x => x.Id == magicLinkId, cancellationToken);

            if (link == null || link.FechaDeUso != null || link.FechaDeVencimiento <= ahoraUtc)
            {
                return false;
            }

            link.FechaDeUso = ahoraUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally
        {
            semaforo.Release();
        }
    }
}
