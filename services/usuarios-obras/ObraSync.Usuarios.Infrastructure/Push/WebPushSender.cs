using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Settings;
using ObraSync.Usuarios.Domain.Entities;
using Polly;
using Polly.Retry;

namespace ObraSync.Usuarios.Infrastructure.Push;

public class WebPushSender : IWebPushSender
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PushServiceClient _pushClient;
    private readonly VapidSettings _vapidSettings;
    private readonly ResiliencePipeline<ResultadoEnvioWebPush> _resiliencePipeline;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(
        HttpClient httpClient,
        IOptions<VapidSettings> vapidOptions,
        ILogger<WebPushSender>? logger = null,
        TimeSpan? esperaBaseRetry = null,
        int maxReintentos = 3,
        bool usarJitter = true)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _pushClient = new PushServiceClient(httpClient)
        {
            AutoRetryAfter = false
        };
        _vapidSettings = vapidOptions?.Value ?? throw new ArgumentNullException(nameof(vapidOptions));
        _logger = logger ?? NullLogger<WebPushSender>.Instance;
        _resiliencePipeline = CrearPipeline(
            esperaBaseRetry ?? TimeSpan.FromMilliseconds(500),
            maxReintentos,
            usarJitter);
    }

    public async Task<ResultadoEnvioWebPush> EnviarAsync(
        SuscripcionPush suscripcion,
        PayloadNotificacionPushDto payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suscripcion);
        ArgumentNullException.ThrowIfNull(payload);

        if (!_vapidSettings.EstaConfigurado())
        {
            return new ResultadoEnvioWebPush(
                EstadoEnvioWebPush.NoConfigurado,
                Error: "Las claves VAPID no están configuradas.");
        }

        var bodyJson = JsonSerializer.Serialize(payload, JsonOptions);

        return await _resiliencePipeline.ExecuteAsync(
            async ct => await EnviarUnIntentoAsync(suscripcion, bodyJson, ct),
            cancellationToken);
    }

    private async Task<ResultadoEnvioWebPush> EnviarUnIntentoAsync(
        SuscripcionPush suscripcion,
        string bodyJson,
        CancellationToken cancellationToken)
    {
        var pushSubscription = new PushSubscription
        {
            Endpoint = suscripcion.Endpoint
        };
        pushSubscription.SetKey(PushEncryptionKeyName.P256DH, suscripcion.P256dh);
        pushSubscription.SetKey(PushEncryptionKeyName.Auth, suscripcion.Auth);

        var message = new PushMessage(bodyJson)
        {
            Urgency = PushMessageUrgency.Normal
        };

        using var vapidAuth = new VapidAuthentication(
            _vapidSettings.PublicKey.Trim(),
            _vapidSettings.PrivateKey.Trim())
        {
            Subject = _vapidSettings.Subject.Trim()
        };

        try
        {
            await _pushClient.RequestPushMessageDeliveryAsync(
                pushSubscription,
                message,
                vapidAuth,
                cancellationToken);

            return new ResultadoEnvioWebPush(EstadoEnvioWebPush.Enviado, StatusCode: 201);
        }
        catch (PushServiceClientException ex)
        {
            var statusCode = (int)ex.StatusCode;
            if (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return new ResultadoEnvioWebPush(
                    EstadoEnvioWebPush.SuscripcionExpirada,
                    StatusCode: statusCode,
                    Error: "Suscripción expirada o revocada por el servidor push.");
            }

            if (statusCode is 408 or 429 or >= 500)
            {
                return new ResultadoEnvioWebPush(
                    EstadoEnvioWebPush.ErrorTransitorio,
                    StatusCode: statusCode,
                    Error: $"Error transitorio del servidor push (HTTP {statusCode}).");
            }

            return new ResultadoEnvioWebPush(
                EstadoEnvioWebPush.ErrorPermanente,
                StatusCode: statusCode,
                Error: $"El servidor push rechazó la entrega (HTTP {statusCode}).");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogDebug(
                ex,
                "Falla transitoria de red intentando enviar Web Push a Suscripcion {SuscripcionId}.",
                suscripcion.Id);

            return new ResultadoEnvioWebPush(
                EstadoEnvioWebPush.ErrorTransitorio,
                Error: ex.GetType().Name);
        }
    }

    private static ResiliencePipeline<ResultadoEnvioWebPush> CrearPipeline(
        TimeSpan esperaBase,
        int maxReintentos,
        bool usarJitter)
    {
        var builder = new ResiliencePipelineBuilder<ResultadoEnvioWebPush>();

        if (maxReintentos > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<ResultadoEnvioWebPush>
            {
                MaxRetryAttempts = maxReintentos,
                Delay = esperaBase,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = usarJitter,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Result?.Estado == EstadoEnvioWebPush.ErrorTransitorio)
            });
        }

        return builder.Build();
    }
}
