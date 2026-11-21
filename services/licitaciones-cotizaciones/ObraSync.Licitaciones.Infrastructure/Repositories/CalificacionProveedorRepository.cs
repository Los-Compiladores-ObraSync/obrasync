using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.DTOs;
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
        var resumenes = await GetResumenesByProveedorIdsAsync(proveedorIds, cancellationToken);
        return resumenes.ToDictionary(kv => kv.Key, kv => kv.Value.Promedio);
    }

    public async Task<IReadOnlyDictionary<Guid, ResumenCalificacionProveedor>> GetResumenesByProveedorIdsAsync(
        IReadOnlyCollection<Guid> proveedorIds,
        CancellationToken cancellationToken = default)
    {
        if (proveedorIds == null || proveedorIds.Count == 0)
        {
            return new Dictionary<Guid, ResumenCalificacionProveedor>();
        }

        var ids = proveedorIds.Distinct().ToList();
        var resumenes = await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => ids.Contains(x.ProveedorId))
            .GroupBy(x => x.ProveedorId)
            .Select(g => new
            {
                ProveedorId = g.Key,
                Promedio = g.Average(x => (double)x.Calificacion),
                Cantidad = g.Count(),
                Fecha = g.Max(x => x.FechaActualizacion)
            })
            .ToListAsync(cancellationToken);

        return resumenes.ToDictionary(
            x => x.ProveedorId,
            x => new ResumenCalificacionProveedor(
                x.ProveedorId,
                Math.Round((decimal)x.Promedio, 2),
                x.Cantidad,
                x.Fecha));
    }

    public async Task<CalificacionProveedor> UpsertAsync(
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default)
    {
        var fechaUtc = NormalizarAUtc(fechaActualizacionUtc);

        // Aislamiento legacy (#88): solo busca/actualiza filas globales (OrdenCompraId == null)
        // para no pisar nunca calificaciones asociadas a órdenes de compra (#222).
        var existente = await _context.CalificacionesProveedores
            .FirstOrDefaultAsync(
                x => x.ProveedorId == proveedorId && x.CalificadorId == calificadorId && x.OrdenCompraId == null,
                cancellationToken);

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
            OrdenCompraId = null,
            CalificadorId = calificadorId,
            Calificacion = calificacion,
            Comentario = null,
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
            _context.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _context.CalificacionesProveedores
                .FirstAsync(
                    x => x.ProveedorId == proveedorId && x.CalificadorId == calificadorId && x.OrdenCompraId == null,
                    cancellationToken);

            ganadora.Calificacion = calificacion;
            ganadora.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return ganadora;
        }
    }

    public async Task<CalificacionProveedor> UpsertPorOrdenAsync(
        Guid ordenCompraId,
        Guid proveedorId,
        Guid calificadorId,
        int calificacion,
        string? comentario,
        DateTime fechaActualizacionUtc,
        CancellationToken cancellationToken = default)
    {
        var fechaUtc = NormalizarAUtc(fechaActualizacionUtc);

        var existente = await _context.CalificacionesProveedores
            .FirstOrDefaultAsync(
                x => x.OrdenCompraId == ordenCompraId && x.CalificadorId == calificadorId,
                cancellationToken);

        if (existente != null)
        {
            existente.Calificacion = calificacion;
            existente.Comentario = comentario;
            existente.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return existente;
        }

        var nueva = new CalificacionProveedor
        {
            Id = Guid.NewGuid(),
            ProveedorId = proveedorId,
            OrdenCompraId = ordenCompraId,
            CalificadorId = calificadorId,
            Calificacion = calificacion,
            Comentario = comentario,
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
            _context.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _context.CalificacionesProveedores
                .FirstAsync(
                    x => x.OrdenCompraId == ordenCompraId && x.CalificadorId == calificadorId,
                    cancellationToken);

            ganadora.Calificacion = calificacion;
            ganadora.Comentario = comentario;
            ganadora.FechaActualizacion = fechaUtc;
            await _context.SaveChangesAsync(cancellationToken);
            return ganadora;
        }
    }

    public async Task<CalificacionProveedor?> GetByOrdenYUsuarioAsync(
        Guid ordenCompraId,
        Guid calificadorId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CalificacionesProveedores
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrdenCompraId == ordenCompraId && x.CalificadorId == calificadorId,
                cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, CalificacionProveedor>> GetByOrdenIdsYUsuarioAsync(
        IReadOnlyCollection<Guid> ordenCompraIds,
        Guid calificadorId,
        CancellationToken cancellationToken = default)
    {
        if (ordenCompraIds == null || ordenCompraIds.Count == 0 || calificadorId == Guid.Empty)
        {
            return new Dictionary<Guid, CalificacionProveedor>();
        }

        var ids = ordenCompraIds.Distinct().ToList();
        var filas = await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => x.OrdenCompraId.HasValue && ids.Contains(x.OrdenCompraId.Value) && x.CalificadorId == calificadorId)
            .ToListAsync(cancellationToken);

        return filas.ToDictionary(x => x.OrdenCompraId!.Value, x => x);
    }

    public async Task<IReadOnlyList<ComentarioCalificacionProveedorDto>> GetUltimosComentariosAsync(
        Guid proveedorId,
        int limite = 20,
        CancellationToken cancellationToken = default)
    {
        var filas = await _context.CalificacionesProveedores
            .AsNoTracking()
            .Where(x => x.ProveedorId == proveedorId && x.Comentario != null && x.Comentario != "")
            .OrderByDescending(x => x.FechaActualizacion)
            .ThenByDescending(x => x.Id)
            .Take(limite)
            .ToListAsync(cancellationToken);

        return filas
            .Where(x => !string.IsNullOrWhiteSpace(x.Comentario))
            .Select(x => new ComentarioCalificacionProveedorDto(
                OrdenCompraId: x.OrdenCompraId,
                Calificacion: x.Calificacion,
                Comentario: x.Comentario!.Trim(),
                FechaActualizacion: NormalizarAUtc(x.FechaActualizacion)))
            .ToList();
    }

    private static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };

    private static bool EsDuplicadoCalificacion(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
