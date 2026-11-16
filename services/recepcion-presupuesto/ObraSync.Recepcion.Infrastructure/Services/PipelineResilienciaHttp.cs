using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ObraSync.Recepcion.Infrastructure.Services;

/// <summary>
/// Construye y expone la política de resiliencia con Polly v8 (#103):
/// 1. Reintentos con retroceso exponencial y jitter ante fallas transitorias (5xx, timeouts, caídas de red).
/// 2. Circuit breaker que corta el tráfico cuando el servicio destino supera el umbral de fallas consecutivas o ratio de fallas.
/// No reintenta errores del cliente ni autorización (400, 401, 403, 404).
/// </summary>
public static class PipelineResilienciaHttp
{
    public static ResiliencePipeline<HttpResponseMessage> Crear(ResilienciaHttpOptions? opciones = null)
    {
        var opts = opciones ?? new ResilienciaHttpOptions();
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();

        // Capa externa: reintentos con retroceso exponencial si MaxReintentos > 0.
        if (opts.MaxReintentos > 0)
        {
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = opts.MaxReintentos,
                Delay = TimeSpan.FromMilliseconds(opts.EsperaBaseMs),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = opts.UsarJitter,
                ShouldHandle = args => ValueTask.FromResult(
                    EsFallaTransitoria(args.Outcome) && !EsNombreInexistente(args.Outcome)),
                OnRetry = args =>
                {
                    // La respuesta fallida se libera antes del siguiente intento para no fugar recursos.
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            });
        }

        // Capa interna: si el servicio destino sigue fallando, el circuit breaker se abre y corta rápido.
        builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            FailureRatio = opts.RatioFallas,
            MinimumThroughput = opts.MinimoPedidos,
            SamplingDuration = TimeSpan.FromSeconds(opts.VentanaSegundos),
            BreakDuration = TimeSpan.FromSeconds(opts.PausaSegundos),
            ShouldHandle = args => ValueTask.FromResult(EsFallaTransitoria(args.Outcome))
        });

        return builder.Build();
    }

    /// <summary>
    /// Determina si un resultado o excepción califica como falla transitoria elegible para reintento y circuit breaker:
    /// - Excepciones de red y timeouts: HttpRequestException, TimeoutException, TaskCanceledException.
    /// - Códigos de estado HTTP transitorios: 5xx (500, 502, 503, 504) o 408 (Request Timeout).
    /// Los códigos 4xx (400, 401, 403, 404) NO son fallas transitorias y nunca se deben reintentar.
    /// </summary>
    public static bool EsFallaTransitoria(Outcome<HttpResponseMessage> resultado)
    {
        if (resultado.Exception is HttpRequestException)
        {
            return true;
        }

        if (resultado.Exception is TimeoutException)
        {
            return true;
        }

        if (resultado.Exception is TaskCanceledException)
        {
            return true;
        }

        if (resultado.Result is { } respuesta)
        {
            var codigo = (int)respuesta.StatusCode;
            return codigo >= 500 || codigo == (int)HttpStatusCode.RequestTimeout;
        }

        return false;
    }

    /// <summary>
    /// Si el nombre DNS del host no existe (por ejemplo contenedor apagado o error de configuración),
    /// no tiene sentido reintentar en bucle ya que cada intento demoraría segundos.
    /// Igual computa como falla para el circuit breaker.
    /// </summary>
    public static bool EsNombreInexistente(Outcome<HttpResponseMessage> resultado)
    {
        return resultado.Exception?.InnerException is SocketException
        {
            SocketErrorCode: SocketError.HostNotFound
        };
    }
}
