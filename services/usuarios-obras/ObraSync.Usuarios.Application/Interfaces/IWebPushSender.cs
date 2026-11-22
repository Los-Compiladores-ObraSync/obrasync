using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;

namespace ObraSync.Usuarios.Application.Interfaces;

public enum EstadoEnvioWebPush
{
    Enviado,
    SuscripcionExpirada,
    ErrorTransitorio,
    ErrorPermanente,
    NoConfigurado
}

public record ResultadoEnvioWebPush(
    EstadoEnvioWebPush Estado,
    int? StatusCode = null,
    string? Error = null
);

public interface IWebPushSender
{
    Task<ResultadoEnvioWebPush> EnviarAsync(
        SuscripcionPush suscripcion,
        PayloadNotificacionPushDto payload,
        CancellationToken cancellationToken = default);
}
