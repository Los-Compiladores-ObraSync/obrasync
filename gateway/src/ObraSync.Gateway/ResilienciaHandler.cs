using Polly;

namespace ObraSync.Gateway;

/// <summary>
/// Filtro por el que pasa cada llamada del Gateway hacia un servicio.
/// Ejecuta la llamada dentro del pipeline de Polly (reintentos + circuit breaker).
/// </summary>
public sealed class ResilienciaHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public ResilienciaHandler(ResiliencePipeline<HttpResponseMessage> pipeline, HttpMessageHandler handlerInterno)
        : base(handlerInterno)
    {
        _pipeline = pipeline;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contexto = ResilienceContextPool.Shared.Get(cancellationToken);
        try
        {
            contexto.Properties.Set(PipelineDeResiliencia.EsLectura, EsMetodoDeLectura(request.Method));

            return await _pipeline.ExecuteAsync(
                ctx => new ValueTask<HttpResponseMessage>(EnviarAlServicio(request, ctx.CancellationToken)),
                contexto);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(contexto);
        }
    }

    private Task<HttpResponseMessage> EnviarAlServicio(HttpRequestMessage request, CancellationToken cancellationToken)
        => base.SendAsync(request, cancellationToken);

    private static bool EsMetodoDeLectura(HttpMethod metodo)
        => metodo == HttpMethod.Get || metodo == HttpMethod.Head;
}
