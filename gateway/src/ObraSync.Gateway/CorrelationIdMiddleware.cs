namespace ObraSync.Gateway;

/// <summary>
/// Garantiza que todo pedido que pasa por el Gateway lleve un X-Correlation-ID.
/// Si el cliente ya mandó uno válido se respeta; si no, se genera uno nuevo.
/// YARP reenvía los encabezados del pedido, así que los servicios lo reciben.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const int LongitudMaxima = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        // Si no viene, o viene vacío o demasiado largo, generamos uno nuevo.
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > LongitudMaxima)
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = correlationId;
        }

        // Lo devolvemos también en la respuesta para poder rastrear el pedido desde el cliente.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Todos los logs de este pedido incluyen el CorrelationId.
        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
