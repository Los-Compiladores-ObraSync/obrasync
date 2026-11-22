using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface ISuscripcionPushRepository
{
    Task<(SuscripcionPush Suscripcion, bool EsNueva)> UpsertConLimiteAsync(
        Guid usuarioId,
        string endpoint,
        string p256dh,
        string auth,
        string? navegador,
        bool permitirHttpLocalhost,
        DateTime ahoraUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuscripcionPush>> GetByUsuarioIdsAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken = default);

    Task<SuscripcionPush?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SuscripcionPush?> GetByUsuarioYEndpointAsync(
        Guid usuarioId,
        string endpoint,
        CancellationToken cancellationToken = default);

    Task EliminarAsync(SuscripcionPush suscripcion, CancellationToken cancellationToken = default);

    Task ActualizarUltimosEnviosAsync(
        IReadOnlyCollection<Guid> suscripcionIds,
        DateTime fechaUtc,
        CancellationToken cancellationToken = default);

    Task EliminarPorIdsAsync(
        IReadOnlyCollection<Guid> suscripcionIds,
        CancellationToken cancellationToken = default);
}
