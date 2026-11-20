namespace ObraSync.Usuarios.Api.Middlewares;

/// <summary>
/// Propaga y registra el identificador de correlación (X-Correlation-ID) (#104).
/// Respeta el valor que manda el Gateway (hasta 64 caracteres, solo letras, números, guion y guion bajo) o genera un GUID nuevo,
/// lo devuelve en la respuesta y abre un scope de log con "CorrelationId" para que todos los logs del pedido lo incluyan.
/// Va ANTES del middleware de excepciones: así hasta los errores quedan con el mismo id.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
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
        var recibido = context.Request.Headers[CorrelationIdHeader].FirstOrDefault()?.Trim();

        // Un valor vacío, largo o con caracteres raros se reemplaza: así nadie puede meter saltos de línea en los logs.
        var correlationId = EsValido(recibido) ? recibido! : Guid.NewGuid().ToString();

        context.Request.Headers[CorrelationIdHeader] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;
        // Por si más adelante alguien más pisa el encabezado: se vuelve a poner justo antes de enviar la respuesta.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            _logger.LogInformation(
                "Procesando solicitud HTTP {Method} {Path} con CorrelationId: {CorrelationId}",
                context.Request.Method, context.Request.Path, correlationId);

            await _next(context);
        }
    }

    private static bool EsValido(string? valor) =>
        !string.IsNullOrWhiteSpace(valor)
        && valor.Length <= LongitudMaxima
        && valor.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
