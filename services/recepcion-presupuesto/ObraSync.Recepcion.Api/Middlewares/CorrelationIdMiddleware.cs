using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ObraSync.Recepcion.Api.Middlewares;

/// <summary>
/// Middleware para propagar y registrar el identificador de correlación (X-Correlation-ID) (#104).
/// Si la petición HTTP entrante no incluye el encabezado o no es un GUID válido, genera uno nuevo.
/// Inyecta el encabezado en la respuesta HTTP y abre un scope de log con 'CorrelationId' para toda la petición.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationIdHeader = "X-Correlation-ID";
    public const string CorrelationIdItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headerValue = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        string correlationId;

        if (Guid.TryParse(headerValue, out var parsedGuid) && parsedGuid != Guid.Empty)
        {
            correlationId = parsedGuid.ToString();
        }
        else
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers[CorrelationIdHeader] = correlationId;
        }

        context.Items[CorrelationIdItemKey] = correlationId;

        // Asegurar que la respuesta HTTP contenga el encabezado X-Correlation-ID
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        if (!context.Response.Headers.ContainsKey(CorrelationIdHeader))
        {
            context.Response.Headers[CorrelationIdHeader] = correlationId;
        }

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            _logger.LogInformation(
                "Procesando solicitud HTTP {Method} {Path} con CorrelationId: {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                correlationId);

            await _next(context);
        }
    }
}
