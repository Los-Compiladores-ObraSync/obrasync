using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace ObraSync.Gateway.Tests;

/// <summary>
/// Levanta el gateway completo en memoria, pero cambia el "cliente HTTP" de YARP por uno falso.
/// Así ningún pedido sale a la red y podemos ver exactamente qué recibiría el servicio de destino.
/// </summary>
public class GatewayFactory : WebApplicationFactory<Program>
{
    public ServicioDestinoFalso Destino { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(new ClienteHttpFalsoFactory(Destino));
        });
    }
}

/// <summary>Lo que el servicio de destino recibió del gateway.</summary>
public record PedidoRecibido(Uri Uri, string? CorrelationId);

/// <summary>Hace de servicio de destino: anota el último pedido que le llegó y responde 200.</summary>
public class ServicioDestinoFalso : HttpMessageHandler
{
    public PedidoRecibido? UltimoPedido { get; private set; }

    /// <summary>Código con el que responde el servicio falso (por defecto 200).</summary>
    public HttpStatusCode Estado { get; set; } = HttpStatusCode.OK;

    /// <summary>Si tiene valor, el servicio falso "se cae": lanza esta excepción en vez de responder.</summary>
    public Exception? Falla { get; set; }

    public void Reiniciar()
    {
        UltimoPedido = null;
        Estado = HttpStatusCode.OK;
        Falla = null;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryGetValues("X-Correlation-ID", out var valores);
        UltimoPedido = new PedidoRecibido(request.RequestUri!, valores?.FirstOrDefault());

        if (Falla is not null)
        {
            throw Falla;
        }

        return Task.FromResult(new HttpResponseMessage(Estado));
    }
}

public class ClienteHttpFalsoFactory : IForwarderHttpClientFactory
{
    private readonly HttpMessageHandler _handler;

    public ClienteHttpFalsoFactory(HttpMessageHandler handler) => _handler = handler;

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) =>
        new(_handler, disposeHandler: false);
}
