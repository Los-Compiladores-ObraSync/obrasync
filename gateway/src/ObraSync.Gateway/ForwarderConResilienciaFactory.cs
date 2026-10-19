using System.Collections.Concurrent;
using Polly;
using Yarp.ReverseProxy.Forwarder;

namespace ObraSync.Gateway;

/// <summary>
/// Le dice a YARP cómo armar el cliente HTTP con el que llama a cada servicio: el de siempre, envuelto con Polly.
/// Hay un pipeline (y por lo tanto un circuit breaker) por servicio: si uno se cae, los demás siguen andando.
/// </summary>
public sealed class ForwarderConResilienciaFactory : ForwarderHttpClientFactory
{
    private readonly ResilienciaOptions _opciones;

    // Se guardan por servicio para que el estado del breaker no se pierda si YARP recarga la configuración.
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _pipelines = new();

    public ForwarderConResilienciaFactory(ILogger<ForwarderHttpClientFactory> logger, ResilienciaOptions opciones)
        : base(logger)
    {
        _opciones = opciones;
    }

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
    {
        var handlerBase = base.WrapHandler(context, handler);
        var pipeline = _pipelines.GetOrAdd(context.ClusterId, _ => PipelineDeResiliencia.Crear(_opciones));
        return new ResilienciaHandler(pipeline, handlerBase);
    }
}
