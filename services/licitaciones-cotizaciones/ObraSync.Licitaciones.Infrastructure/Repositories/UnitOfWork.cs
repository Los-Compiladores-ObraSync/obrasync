using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly LicitacionesDbContext _context;

    public UnitOfWork(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    private static bool EsDuplicadoDeOrdenPorLicitacion(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sql
        && sql.Message.Contains("IX_OrdenesCompra_LicitacionId", StringComparison.Ordinal);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (EsDuplicadoDeOrdenPorLicitacion(ex))
        {
            // 2601/2627 = violación de índice único. Solo traducimos el de OrdenesCompra.LicitacionId:
            // significa que otra adjudicación se coló primero. El controlador lo devuelve como 409.
            throw new InvalidOperationException("La licitación ya se encuentra adjudicada o cerrada.", ex);
        }
    }
}