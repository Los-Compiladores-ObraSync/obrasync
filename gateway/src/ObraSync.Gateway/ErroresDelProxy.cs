using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy.Forwarder;

namespace ObraSync.Gateway;

/// <summary>
/// Cuando YARP no logra comunicarse con un servicio (caído, sin red, tardó demasiado) devuelve
/// un 502 o un 504 SIN cuerpo. Acá se completa esa respuesta con un ProblemDetails, igual que el
/// resto de la API, para que el frontend tenga algo claro que mostrar y se pueda rastrear con
/// el correlationId.
/// Solo toca las fallas del propio proxy: si el servicio sí contestó (aunque sea con un 500),
/// su respuesta pasa tal cual.
/// </summary>
public static class ErroresDelProxy
{
    public static async Task ResponderConProblemDetailsAsync(HttpContext context, Func<Task> next)
    {
        await next();

        // YARP deja anotado el motivo cuando no pudo reenviar el pedido.
        var error = context.Features.Get<IForwarderErrorFeature>();
        if (error is null || context.Response.HasStarted)
        {
            return;
        }

        var estado = context.Response.StatusCode;
        if (estado != StatusCodes.Status502BadGateway && estado != StatusCodes.Status504GatewayTimeout)
        {
            return;
        }

        var tiempoAgotado = estado == StatusCodes.Status504GatewayTimeout;
        var problema = new ProblemDetails
        {
            Type = tiempoAgotado
                ? "https://tools.ietf.org/html/rfc9110#section-15.6.5"
                : "https://tools.ietf.org/html/rfc9110#section-15.6.3",
            Title = tiempoAgotado ? "Tiempo de espera agotado" : "Servicio no disponible",
            Status = estado,
            Detail = tiempoAgotado
                ? "El servicio tardó demasiado en responder. Intentá de nuevo en unos instantes."
                : "No se pudo comunicar con el servicio. Intentá de nuevo en unos instantes.",
            Instance = context.Request.Path,
        };
        problema.Extensions["correlationId"] =
            context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();

        await context.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
    }
}
