using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class CalificacionProveedorRepository : ICalificacionProveedorRepository
{
    private readonly LicitacionesDbContext _context;

    public CalificacionProveedorRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CalificacionProveedor?> GetByProveedorIdAsync(
        Guid proveedorId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CalificacionesProveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProveedorId == proveedorId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetCalificacionesByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default)
    {
        if (proveedorIds == null || proveedorIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = proveedorIds.Distinct().ToList();
        return await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => ids.Contains(x.ProveedorId))
            .ToDictionaryAsync(x => x.ProveedorId, x => x.Calificacion, cancellationToken);
    }

    public async Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default)
    {
        var fechaUtc = fechaActualizacionUtc.Kind == DateTimeKind.Utc
            ? fechaActualizacionUtc
            : DateTime.SpecifyKind(fechaActualizacionUtc, DateTimeKind.Utc);

        var existente = await _context.CalificacionesProveedores
            .FirstOrDefaultAsync(x => x.ProveedorId == proveedorId, cancellationToken);

        if (existente != null)
        {
            existente.Calificacion = calificacion;
            existente.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return existente;
        }

        var nueva = new CalificacionProveedor
        {
            Id = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Calificacion = calificacion,
            FechaActualizacion = fechaUtc
        };

        _context.CalificacionesProveedores.Add(nueva);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return nueva;
        }
        catch (DbUpdateException ex) when (EsDuplicadoProveedor(ex))
        {
            // Colisión de índice único en alta concurrente: descartar la inserción local y actualizar la fila ganadora.
            _context.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _context.CalificacionesProveedores
                .FirstAsync(x => x.ProveedorId == proveedorId, cancellationToken);

            ganadora.Calificacion = calificacion;
            ganadora.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return ganadora;
        }
    }

    private static bool EsDuplicadoProveedor(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
