using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ObraSync.Gateway;

/// <summary>Arma el pipeline de Polly: primero reintentos (solo GET/HEAD) y adentro el circuit breaker.</summary>
public static class PipelineDeResiliencia
{
    /// <summary>
    /// Marca que se le pone a cada pedido para que el reintento sepa si es una lectura.
    /// Un POST reintentado podría duplicar una requisición o una orden de compra, por eso solo se reintenta lo que no modifica datos.
    /// </summary>
    internal static readonly ResiliencePropertyKey<bool> EsLectura = new("obrasync.es-lectura");

    public static ResiliencePipeline<HttpResponseMessage> Crear(ResilienciaOptions opciones)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            // Afuera: reintentos con retroceso exponencial. Cada intento pasa por el breaker.
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = opciones.MaxReintentos,
                Delay = TimeSpan.FromMilliseconds(opciones.EsperaBaseMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Context.Properties.TryGetValue(EsLectura, out var esLectura)
                    && esLectura
                    && EsFalla(args.Outcome)),
                OnRetry = args =>
                {
                    // La respuesta fallida no se usa más: se libera antes de volver a intentar.
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            })
            // Adentro: si el servicio falla demasiado, se corta y se responde enseguida sin esperar.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = opciones.RatioFallas,
                MinimumThroughput = opciones.MinimoPedidos,
                SamplingDuration = TimeSpan.FromSeconds(opciones.VentanaSegundos),
                BreakDuration = TimeSpan.FromSeconds(opciones.PausaSegundos),
                ShouldHandle = args => ValueTask.FromResult(EsFalla(args.Outcome))
            })
            .Build();
    }

    /// <summary>
    /// Qué cuenta como falla: no se pudo conectar, o el servicio respondió 502/503/504.
    /// Un 500 NO cuenta: es un error del código del servicio y repetir el pedido no lo arregla.
    /// </summary>
    private static bool EsFalla(Outcome<HttpResponseMessage> resultado)
    {
        if (resultado.Exception is HttpRequestException)
        {
            return true;
        }

        return resultado.Result is { StatusCode: System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout };
    }
}
