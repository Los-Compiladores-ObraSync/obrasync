using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Domain.Exceptions;

namespace ObraSync.Usuarios.Application.Services;

public class SuscripcionPushService : ISuscripcionPushService
{
    private readonly ISuscripcionPushRepository _repository;
    private readonly VapidSettings _vapidSettings;

    public SuscripcionPushService(
        ISuscripcionPushRepository repository,
        IOptions<VapidSettings> vapidOptions)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _vapidSettings = vapidOptions?.Value ?? throw new ArgumentNullException(nameof(vapidOptions));
    }

    public VapidPublicKeyResponseDto ObtenerClavePublicaVapid()
    {
        if (!_vapidSettings.TieneClavePublica)
        {
            throw new ServicioNoDisponibleException(
                "Las notificaciones Web Push no están configuradas en el servidor (falta Vapid:PublicKey).");
        }

        return new VapidPublicKeyResponseDto(_vapidSettings.PublicKey.Trim());
    }

    public async Task<(SuscripcionPushResponseDto Suscripcion, bool EsNueva)> RegistrarSuscripcionAsync(
        Guid usuarioId,
        CrearSuscripcionPushRequestDto request,
        string? userAgentHeader,
        bool permitirHttpLocalhost,
        CancellationToken cancellationToken = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("El token JWT no contiene un identificador de usuario válido.");
        }

        if (request is null)
        {
            throw new ValidacionException("El cuerpo de la petición es obligatorio.");
        }

        var navegadorEfectivo = string.IsNullOrWhiteSpace(request.Navegador)
            ? SuscripcionPush.TruncarNavegador(userAgentHeader)
            : request.Navegador;

        var (entidad, esNueva) = await _repository.UpsertConLimiteAsync(
            usuarioId: usuarioId,
            endpoint: request.Endpoint ?? string.Empty,
            p256dh: request.P256dh ?? string.Empty,
            auth: request.Auth ?? string.Empty,
            navegador: navegadorEfectivo,
            permitirHttpLocalhost: permitirHttpLocalhost,
            ahoraUtc: DateTime.UtcNow,
            cancellationToken: cancellationToken);

        return (MapToDto(entidad), esNueva);
    }

    public async Task EliminarPorIdAsync(
        Guid usuarioId,
        Guid suscripcionId,
        CancellationToken cancellationToken = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("El token JWT no contiene un identificador de usuario válido.");
        }

        if (suscripcionId == Guid.Empty)
        {
            throw new ValidacionException("El identificador de la suscripción es obligatorio.");
        }

        var existente = await _repository.GetByIdAsync(suscripcionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la suscripción push con ID {suscripcionId}.");

        if (existente.UsuarioId != usuarioId)
        {
            throw new ForbiddenException("No tenés permisos para eliminar una suscripción push de otro usuario.");
        }

        await _repository.EliminarAsync(existente, cancellationToken);
    }

    public async Task EliminarPorEndpointAsync(
        Guid usuarioId,
        string? endpoint,
        CancellationToken cancellationToken = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("El token JWT no contiene un identificador de usuario válido.");
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new ValidacionException("El parámetro 'endpoint' es obligatorio.");
        }

        var endpointLimpio = endpoint.Trim();
        var existente = await _repository.GetByUsuarioYEndpointAsync(usuarioId, endpointLimpio, cancellationToken)
            ?? throw new KeyNotFoundException("No se encontró una suscripción push propia para el endpoint indicado.");

        await _repository.EliminarAsync(existente, cancellationToken);
    }

    private static SuscripcionPushResponseDto MapToDto(SuscripcionPush entidad) =>
        new(
            Id: entidad.Id,
            UsuarioId: entidad.UsuarioId,
            Endpoint: entidad.Endpoint,
            Navegador: entidad.Navegador,
            FechaAlta: entidad.FechaAlta,
            UltimoEnvio: entidad.UltimoEnvio);
}
