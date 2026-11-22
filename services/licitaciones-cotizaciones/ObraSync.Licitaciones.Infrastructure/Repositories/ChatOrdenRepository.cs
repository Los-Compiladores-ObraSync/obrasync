using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Infrastructure.Repositories;

public class ChatOrdenRepository : IChatOrdenRepository
{
    private readonly LicitacionesDbContext _context;

    public ChatOrdenRepository(LicitacionesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PaginaDe<MensajeChatOrden>> GetMensajesPorOrdenAsync(
        Guid ordenCompraId,
        ParametrosPagina paginacion,
        CancellationToken cancellationToken = default)
    {
        var query = _context.MensajesChatOrden
            .AsNoTracking()
            .Where(m => m.OrdenCompraId == ordenCompraId);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(m => m.FechaCreacion)
            .ThenBy(m => m.Id)
            .Skip(paginacion.Saltear)
            .Take(paginacion.Tamano)
            .ToListAsync(cancellationToken);

        return new PaginaDe<MensajeChatOrden>(items, total);
    }

    public async Task AddMensajeAsync(MensajeChatOrden mensaje, CancellationToken cancellationToken = default)
    {
        await _context.MensajesChatOrden.AddAsync(mensaje, cancellationToken);
    }

    public async Task<LecturaChatOrden> UpsertLecturaAsync(
        Guid ordenCompraId,
        Guid usuarioId,
        DateTime ultimaLecturaUtc,
        CancellationToken cancellationToken = default)
    {
        var fechaUtc = NormalizarAUtc(ultimaLecturaUtc);

        var existente = await _context.LecturasChatOrden
            .FirstOrDefaultAsync(
                x => x.OrdenCompraId == ordenCompraId && x.UsuarioId == usuarioId,
                cancellationToken);

        if (existente is not null)
        {
            if (fechaUtc > existente.UltimaLecturaEn)
            {
                existente.UltimaLecturaEn = fechaUtc;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return existente;
        }

        var nueva = new LecturaChatOrden
        {
            Id = Guid.NewGuid(),
            OrdenCompraId = ordenCompraId,
            UsuarioId = usuarioId,
            UltimaLecturaEn = fechaUtc
        };

        _context.LecturasChatOrden.Add(nueva);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return nueva;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            _context.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _context.LecturasChatOrden
                .FirstAsync(
                    x => x.OrdenCompraId == ordenCompraId && x.UsuarioId == usuarioId,
                    cancellationToken);

            if (fechaUtc > ganadora.UltimaLecturaEn)
            {
                ganadora.UltimaLecturaEn = fechaUtc;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return ganadora;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetConteoNoLeidosPorOrdenIdsAsync(
        IReadOnlyCollection<Guid> ordenCompraIds,
        Guid usuarioId,
        CancellationToken cancellationToken = default)
    {
        if (ordenCompraIds == null || ordenCompraIds.Count == 0 || usuarioId == Guid.Empty)
        {
            return new Dictionary<Guid, int>();
        }

        var ids = ordenCompraIds.Distinct().ToList();
        var query = FiltrarMensajesNoLeidos(
            _context.MensajesChatOrden.AsNoTracking().Where(m => ids.Contains(m.OrdenCompraId)),
            usuarioId);

        var conteos = await query
            .GroupBy(m => m.OrdenCompraId)
            .Select(g => new { OrdenCompraId = g.Key, Cantidad = g.Count() })
            .ToListAsync(cancellationToken);

        return conteos.ToDictionary(x => x.OrdenCompraId, x => x.Cantidad);
    }

    public async Task<IReadOnlyList<NoLeidosOrdenDto>> GetNoLeidosPorUsuarioAsync(
        Guid usuarioId,
        bool soloProveedor,
        CancellationToken cancellationToken = default)
    {
        if (usuarioId == Guid.Empty)
        {
            return Array.Empty<NoLeidosOrdenDto>();
        }

        var baseQuery = _context.MensajesChatOrden.AsNoTracking();
        if (soloProveedor)
        {
            var ordenesDelProveedor = _context.OrdenesCompra
                .AsNoTracking()
                .Where(o => o.ProveedorId == usuarioId)
                .Select(o => o.OrdenCompraId);

            baseQuery = baseQuery.Where(m => ordenesDelProveedor.Contains(m.OrdenCompraId));
        }

        var conteos = await FiltrarMensajesNoLeidos(baseQuery, usuarioId)
            .GroupBy(m => m.OrdenCompraId)
            .Select(g => new
            {
                OrdenCompraId = g.Key,
                Cantidad = g.Count(),
                UltimoMensaje = g.Max(m => m.FechaCreacion)
            })
            .OrderByDescending(x => x.UltimoMensaje)
            .ToListAsync(cancellationToken);

        return conteos
            .Select(x => new NoLeidosOrdenDto(x.OrdenCompraId, x.Cantidad))
            .ToList();
    }

    private IQueryable<MensajeChatOrden> FiltrarMensajesNoLeidos(
        IQueryable<MensajeChatOrden> source,
        Guid usuarioId)
    {
        return source
            .Where(m => m.EsSistema || (m.AutorId != null && m.AutorId != usuarioId))
            .Where(m => !_context.LecturasChatOrden.Any(l =>
                l.OrdenCompraId == m.OrdenCompraId &&
                l.UsuarioId == usuarioId &&
                l.UltimaLecturaEn >= m.FechaCreacion));
    }

    private static DateTime NormalizarAUtc(DateTime fecha) => fecha.Kind switch
    {
        DateTimeKind.Utc => fecha,
        DateTimeKind.Local => fecha.ToUniversalTime(),
        _ => DateTime.SpecifyKind(fecha, DateTimeKind.Utc)
    };
}
