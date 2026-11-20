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

    public async Task<ResumenCalificacionProveedor?> GetResumenAsync(
        Guid proveedorId,
        CancellationToken cancellationToken = default)
    {
        var datos = await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => x.ProveedorId == proveedorId)
            .GroupBy(x => x.ProveedorId)
            .Select(g => new
            {
                ProveedorId = g.Key,
                Promedio = g.Average(x => (double)x.Calificacion),
                Cantidad = g.Count(),
                Fecha = g.Max(x => x.FechaActualizacion)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return datos is null
            ? null
            : new ResumenCalificacionProveedor(
                datos.ProveedorId,
                Math.Round((decimal)datos.Promedio, 2),
                datos.Cantidad,
                datos.Fecha);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetPromediosByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default)
    {
        if (proveedorIds == null || proveedorIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var ids = proveedorIds.Distinct().ToList();
        var promedios = await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => ids.Contains(x.ProveedorId))
            .GroupBy(x => x.ProveedorId)
            .Select(g => new { ProveedorId = g.Key, Promedio = g.Average(x => (double)x.Calificacion) })
            .ToListAsync(cancellationToken);

        return promedios.ToDictionary(x => x.ProveedorId, x => Math.Round((decimal)x.Promedio, 2));
    }

    public async Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default)
    {
        var fechaUtc = fechaActualizacionUtc.Kind == DateTimeKind.Utc
            ? fechaActualizacionUtc
            : DateTime.SpecifyKind(fechaActualizacionUtc, DateTimeKind.Utc);

        var existente = await _context.CalificacionesProveedores
            .FirstOrDefaultAsync(x => x.ProveedorId == proveedorId && x.CalificadorId == calificadorId, cancellationToken);

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
            CalificadorId = calificadorId,
            Calificacion = calificacion,
            FechaActualizacion = fechaUtc
        };

        _context.CalificacionesProveedores.Add(nueva);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return nueva;
        }
        catch (DbUpdateException ex) when (EsDuplicadoCalificacion(ex))
        {
            // Colisión de índice único (mismo usuario y proveedor) en alta concurrente: descartar la inserción local y actualizar la fila ganadora.
            _context.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _context.CalificacionesProveedores
                .FirstAsync(x => x.ProveedorId == proveedorId && x.CalificadorId == calificadorId, cancellationToken);

            ganadora.Calificacion = calificacion;
            ganadora.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return ganadora;
        }
    }

    private static bool EsDuplicadoCalificacion(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
