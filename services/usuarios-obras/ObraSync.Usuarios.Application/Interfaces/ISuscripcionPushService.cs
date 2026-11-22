using ObraSync.Usuarios.Application.DTOs;

namespace ObraSync.Usuarios.Application.Interfaces;

public interface ISuscripcionPushService
{
    VapidPublicKeyResponseDto ObtenerClavePublicaVapid();

    Task<(SuscripcionPushResponseDto Suscripcion, bool EsNueva)> RegistrarSuscripcionAsync(
        Guid usuarioId,
        CrearSuscripcionPushRequestDto request,
        string? userAgentHeader,
        bool permitirHttpLocalhost,
        CancellationToken cancellationToken = default);

    Task EliminarPorIdAsync(
        Guid usuarioId,
        Guid suscripcionId,
        CancellationToken cancellationToken = default);

    Task EliminarPorEndpointAsync(
        Guid usuarioId,
        string? endpoint,
        CancellationToken cancellationToken = default);
}
