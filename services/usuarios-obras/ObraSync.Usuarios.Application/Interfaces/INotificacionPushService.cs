using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface INotificacionPushService
{
    Task EnviarAUsuariosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        PayloadNotificacionPushDto payload,
        CancellationToken cancellationToken = default);
}
