using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace ObraSync.Gateway;

/// <summary>
/// Suppresses only YARP HttpForwarder's event 9 for requests that carry a secret in the URL: Magic Link
/// (cotizar y ver el pliego) and the chat hub (#225, JWT in ?access_token=). YARP 2.3.0
/// formats the destination's absolute URI into that event. Other events and routes
/// pass through the original logger unchanged.
/// </summary>
public sealed class MagicLinkForwarderLogger<T> : ILogger<T>
{
    private const int ForwardingEventId = 9;
    private const string ForwarderCategory = "Yarp.ReverseProxy.Forwarder.HttpForwarder";

    private readonly ILogger _inner;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MagicLinkForwarderLogger(ILoggerFactory loggerFactory, IHttpContextAccessor httpContextAccessor)
    {
        _inner = loggerFactory.CreateLogger<T>();
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var context = _httpContextAccessor.HttpContext;
        if (eventId.Id == ForwardingEventId
            && typeof(T).FullName == ForwarderCategory
            && context is not null
            && (MagicLinkGatewayRequest.EsMagicLink(context.Request)
                || ChatHubGatewayRequest.EsChatHub(context.Request)))
        {
            return;
        }

        _inner.Log(logLevel, eventId, state, exception, formatter);
    }
}

public static class MagicLinkGatewayRequest
{
    private const string Prefijo = "/api/licitaciones/magic-links/";

    /// <summary>Cualquier pedido público que lleva el token del enlace en la URL: cotizar (POST) o ver el pliego (GET, #158).</summary>
    public static bool EsMagicLink(HttpRequest request) => EsCotizacion(request) || EsPliego(request);

    /// <summary>GET /api/licitaciones/magic-links/{hash}: el proveedor abre su enlace y ve el pliego. Sin segmentos extra.</summary>
    public static bool EsPliego(HttpRequest request)
    {
        var path = request.Path.Value;
        return HttpMethods.IsGet(request.Method)
            && path is not null
            && path.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase)
            && path.Length > Prefijo.Length
            && path.IndexOf('/', Prefijo.Length) < 0;
    }

    public static bool EsCotizacion(HttpRequest request)
    {
        const string prefijo = "/api/licitaciones/magic-links/";
        const string sufijo = "/cotizaciones";
        var path = request.Path.Value;
        return HttpMethods.IsPost(request.Method)
            && path is not null
            && path.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase)
            && path.Length > prefijo.Length + sufijo.Length;
    }
}
