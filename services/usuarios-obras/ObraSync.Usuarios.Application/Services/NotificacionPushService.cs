using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;

namespace ObraSync.Usuarios.Application.Services;

public class NotificacionPushService : INotificacionPushService
{
    private readonly ISuscripcionPushRepository _repository;
    private readonly IWebPushSender _webPushSender;
    private readonly ILogger<NotificacionPushService> _logger;

    public NotificacionPushService(
        ISuscripcionPushRepository repository,
        IWebPushSender webPushSender,
        ILogger<NotificacionPushService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _webPushSender = webPushSender ?? throw new ArgumentNullException(nameof(webPushSender));
        _logger = logger ?? NullLogger<NotificacionPushService>.Instance;
    }

    public async Task EnviarAUsuariosAsync(
        IReadOnlyCollection<Guid> usuarioIds,
        PayloadNotificacionPushDto payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var destinatarios = usuarioIds?
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        if (destinatarios is null || destinatarios.Count == 0)
        {
            return;
        }

        var suscripciones = await _repository.GetByUsuarioIdsAsync(destinatarios, cancellationToken);
        if (suscripciones.Count == 0)
        {
            return;
        }

        var exitosas = new List<Guid>();
        var expiradas = new List<Guid>();

        foreach (var suscripcion in suscripciones)
        {
            try
            {
                var resultado = await _webPushSender.EnviarAsync(suscripcion, payload, cancellationToken);
                switch (resultado.Estado)
                {
                    case EstadoEnvioWebPush.Enviado:
                        exitosas.Add(suscripcion.Id);
                        break;

                    case EstadoEnvioWebPush.SuscripcionExpirada:
                        expiradas.Add(suscripcion.Id);
                        _logger.LogInformation(
                            "Suscripción push {SuscripcionId} de Usuario {UsuarioId} expiró ({StatusCode}); se programó su baja.",
                            suscripcion.Id,
                            suscripcion.UsuarioId,
                            resultado.StatusCode);
                        break;

                    case EstadoEnvioWebPush.NoConfigurado:
                        _logger.LogWarning(
                            "Envío push omitido para evento {Tipo}: las claves VAPID no están configuradas.",
                            payload.Tipo);
                        return;

                    case EstadoEnvioWebPush.ErrorTransitorio:
                    case EstadoEnvioWebPush.ErrorPermanente:
                        _logger.LogWarning(
                            "No se pudo entregar push a Suscripcion {SuscripcionId} de Usuario {UsuarioId} ({Estado}, StatusCode={StatusCode}).",
                            suscripcion.Id,
                            suscripcion.UsuarioId,
                            resultado.Estado,
                            resultado.StatusCode);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error inesperado enviando notificación push a Suscripcion {SuscripcionId} de Usuario {UsuarioId}. Se continúa con las restantes.",
                    suscripcion.Id,
                    suscripcion.UsuarioId);
            }
        }

        if (exitosas.Count > 0)
        {
            await _repository.ActualizarUltimosEnviosAsync(exitosas, DateTime.UtcNow, cancellationToken);
        }

        if (expiradas.Count > 0)
        {
            await _repository.EliminarPorIdsAsync(expiradas, cancellationToken);
        }
    }
}
