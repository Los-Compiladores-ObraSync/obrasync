using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;

namespace ObraSync.Usuarios.Infrastructure.Repositories;

public class SuscripcionPushRepository : ISuscripcionPushRepository
{
    private readonly AuthDbContext _db;

    public SuscripcionPushRepository(AuthDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<(SuscripcionPush Suscripcion, bool EsNueva)> UpsertConLimiteAsync(
        Guid usuarioId,
        string endpoint,
        string p256dh,
        string auth,
        string? navegador,
        bool permitirHttpLocalhost,
        DateTime ahoraUtc,
        CancellationToken cancellationToken = default)
    {
        var endpointLimpio = SuscripcionPush.ValidarYNormalizarEndpoint(endpoint, permitirHttpLocalhost);
        var endpointHash = SuscripcionPush.CalcularEndpointHash(endpointLimpio);

        var existentes = await _db.SuscripcionesPush
            .Where(s => s.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);

        var existente = existentes.FirstOrDefault(s => s.EndpointHash == endpointHash);
        if (existente is not null)
        {
            existente.Actualizar(p256dh, auth, navegador, ahoraUtc);
            await _db.SaveChangesAsync(cancellationToken);
            return (existente, false);
        }

        // Política acordada (#227): máximo 5 suscripciones por usuario; si entra una 6.ª,
        // reemplazar la más antigua (UltimoEnvio ?? FechaAlta ascendente) dentro de la misma transacción.
        if (existentes.Count >= SuscripcionPush.MaxSuscripcionesPorUsuario)
        {
            var cantidadAEliminar = existentes.Count - SuscripcionPush.MaxSuscripcionesPorUsuario + 1;
            var aEliminar = existentes
                .OrderBy(s => s.UltimoEnvio ?? s.FechaAlta)
                .ThenBy(s => s.FechaAlta)
                .ThenBy(s => s.Id)
                .Take(cantidadAEliminar)
                .ToList();

            _db.SuscripcionesPush.RemoveRange(aEliminar);
        }

        var nueva = SuscripcionPush.Crear(
            usuarioId: usuarioId,
            endpoint: endpointLimpio,
            p256dh: p256dh,
            auth: auth,
            navegador: navegador,
            permitirHttpLocalhost: permitirHttpLocalhost,
            fechaAltaUtc: ahoraUtc);

        _db.SuscripcionesPush.Add(nueva);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (nueva, true);
        }
        catch (DbUpdateException ex) when (EsDuplicadoSql(ex))
        {
            _db.Entry(nueva).State = EntityState.Detached;

            var ganadora = await _db.SuscripcionesPush
                .FirstAsync(s => s.UsuarioId == usuarioId && s.EndpointHash == endpointHash, cancellationToken);

            ganadora.Actualizar(p256dh, auth, navegador, ahoraUtc);
            await _db.SaveChangesAsync(cancellationToken);
            return (ganadora, false);
        }
    }

    public async Task<IReadOnlyList<SuscripcionPush>> GetByUsuarioIdsAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken = default)
    {
        if (usuarioIds == null || usuarioIds.Count == 0)
        {
            return Array.Empty<SuscripcionPush>();
        }

        var ids = usuarioIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<SuscripcionPush>();
        }

        return await _db.SuscripcionesPush
            .AsNoTracking()
            .Where(s => ids.Contains(s.UsuarioId))
            .ToListAsync(cancellationToken);
    }

    public Task<SuscripcionPush?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.SuscripcionesPush.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<SuscripcionPush?> GetByUsuarioYEndpointAsync(
        Guid usuarioId,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        var hash = SuscripcionPush.CalcularEndpointHash(endpoint.Trim());
        return _db.SuscripcionesPush
            .FirstOrDefaultAsync(s => s.UsuarioId == usuarioId && s.EndpointHash == hash, cancellationToken);
    }

    public async Task EliminarAsync(SuscripcionPush suscripcion, CancellationToken cancellationToken = default)
    {
        _db.SuscripcionesPush.Remove(suscripcion);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ActualizarUltimosEnviosAsync(
        IReadOnlyCollection<Guid> suscripcionIds,
        DateTime fechaUtc,
        CancellationToken cancellationToken = default)
    {
        if (suscripcionIds == null || suscripcionIds.Count == 0)
        {
            return;
        }

        var ids = suscripcionIds.Distinct().ToList();
        var entidades = await _db.SuscripcionesPush
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);

        foreach (var entidad in entidades)
        {
            entidad.UltimoEnvio = fechaUtc;
        }

        if (entidades.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task EliminarPorIdsAsync(
        IReadOnlyCollection<Guid> suscripcionIds,
        CancellationToken cancellationToken = default)
    {
        if (suscripcionIds == null || suscripcionIds.Count == 0)
        {
            return;
        }

        var ids = suscripcionIds.Distinct().ToList();
        var entidades = await _db.SuscripcionesPush
            .Where(s => ids.Contains(s.Id))
            .ToListAsync(cancellationToken);

        if (entidades.Count > 0)
        {
            _db.SuscripcionesPush.RemoveRange(entidades);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool EsDuplicadoSql(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
