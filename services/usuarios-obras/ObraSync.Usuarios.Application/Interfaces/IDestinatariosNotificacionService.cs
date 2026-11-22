namespace ObraSync.Usuarios.Application.Interfaces;

public interface IDestinatariosNotificacionService
{
    Task<IReadOnlyList<Guid>> GetUsuarioIdsPorRolesAsync(
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken = default);

    Task<Guid?> GetCapatazIdDeObraAsync(
        Guid obraId,
        CancellationToken cancellationToken = default);

    Task<bool> EsUsuarioActivoAsync(
        Guid usuarioId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> FiltrarUsuariosActivosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        CancellationToken cancellationToken = default);
}
