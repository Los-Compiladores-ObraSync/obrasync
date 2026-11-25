using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.Exceptions;

namespace ObraSync.Recepcion.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly RecepcionDbContext _context;

    public UnitOfWork(RecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return GuardarConTraduccionAsync(() => _context.SaveChangesAsync(cancellationToken));
    }

    internal static async Task<int> GuardarConTraduccionAsync(Func<Task<int>> guardarAsync)
    {
        try
        {
            return await guardarAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrenciaPersistenciaException(ex.Message, ex);
        }
        catch (DbUpdateException ex) when (EsViolacionDeIndiceUnico(ex))
        {
            throw new IndiceUnicoPersistenciaException(ex.Message, ex);
        }
    }

    private static bool EsViolacionDeIndiceUnico(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner == null)
        {
            return false;
        }

        if (inner.GetType().Name == "SqlException" &&
            inner.GetType().GetProperty("Number")?.GetValue(inner) is int sqlNumber)
        {
            return sqlNumber == 2601 || sqlNumber == 2627;
        }

        return false;
    }
}
