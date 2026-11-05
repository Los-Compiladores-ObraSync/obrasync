using ObraSync.Licitaciones.Application.Interfaces;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly LicitacionesDbContext _context;

    public UnitOfWork(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}