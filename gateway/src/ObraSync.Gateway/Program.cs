using ObraSync.Gateway;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Forwarder;

var builder = WebApplication.CreateBuilder(args);

// Resiliencia (Polly): reintentos con retroceso exponencial para los GET y circuit breaker por servicio.
// Se registra ANTES de AddReverseProxy para que YARP use este cliente en lugar del que trae por defecto.
var resiliencia = builder.Configuration.GetSection("Resiliencia").Get<ResilienciaOptions>() ?? new ResilienciaOptions();
builder.Services.AddSingleton(resiliencia);
builder.Services.AddSingleton<IForwarderHttpClientFactory, ForwarderConResilienciaFactory>();

// Propuesta inicial de #159: 5 solicitudes por IP en una ventana deslizante de 1 minuto.
// El equipo debe confirmar estos valores antes de considerarlos definitivos.
var magicLinkRateLimit = builder.Configuration.GetSection("MagicLinkRateLimit").Get<MagicLinkRateLimitOptions>()
    ?? new MagicLinkRateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(httpContext =>
    {
        if (!EsRutaDeCotizacionMagicLink(httpContext.Request))
            return RateLimitPartition.GetNoLimiter<IPAddress>(IPAddress.None);

        return RateLimitPartition.GetSlidingWindowLimiter(
            httpContext.Connection.RemoteIpAddress ?? IPAddress.None,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = magicLinkRateLimit.PermitLimit,
                Window = TimeSpan.FromSeconds(magicLinkRateLimit.WindowSeconds),
                SegmentsPerWindow = magicLinkRateLimit.SegmentsPerWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var cuerpo = System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Demasiadas solicitudes",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Se alcanzó el límite de solicitudes. Intentá de nuevo más tarde.",
            correlationId = context.HttpContext.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString()
        });
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsync(cuerpo, context.HttpContext.RequestAborted);
    };
});

// YARP: carga las rutas y los destinos desde la sección "ReverseProxy" de appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Decorate only ILogger<HttpForwarder> from the pinned YARP 2.3.0 assembly. Its
// event 9 includes the outgoing absolute URI; suppress it only for the Magic Link route.
var tipoHttpForwarder = typeof(IHttpForwarder).Assembly.GetType(
    "Yarp.ReverseProxy.Forwarder.HttpForwarder", throwOnError: true)!;
builder.Services.AddHttpContextAccessor();
builder.Services.Add(ServiceDescriptor.Singleton(
    typeof(ILogger<>).MakeGenericType(tipoHttpForwarder),
    typeof(MagicLinkForwarderLogger<>).MakeGenericType(tipoHttpForwarder)));

var app = builder.Build();

app.UseRouting();

// Tiene que ir ANTES de MapReverseProxy: así el encabezado ya existe cuando YARP reenvía el pedido.
app.UseMiddleware<CorrelationIdMiddleware>();

// Si un servicio no responde, YARP devuelve un 502/504 vacío: acá se completa con un ProblemDetails.
// Va después del Correlation ID (para incluirlo) y antes del proxy.
// Auditoría y limitador preceden a los componentes que pueden finalizar el pedido para registrar también 429 y errores.

// El limitador solo se ejecuta para la operación del Magic Link; no afecta las demás rutas.
// Auditoría sin datos sensibles: no se incluye el segmento {hash}, query string, URL ni cuerpo.
app.Use(async (context, next) =>
{
    if (!EsRutaDeCotizacionMagicLink(context.Request))
    {
        await next();
        return;
    }

    try
    {
        await next();
    }
    finally
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("MagicLinkGateway");
        var estado = context.Response.StatusCode;
        var resultado = estado == StatusCodes.Status429TooManyRequests ? "rate_limited"
            : estado >= 500 ? "proxy_error"
            : estado >= 400 ? "upstream_error"
            : "forwarded";
        logger.LogInformation("Magic Link {Metodo} {Ruta} respondió {Estado}; resultado {Resultado}; CorrelationId {CorrelationId}",
            context.Request.Method,
            "/api/licitaciones/magic-links/{hash}/cotizar",
            estado,
            resultado,
            context.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }
});

app.Use(ErroresDelProxy.ResponderConProblemDetailsAsync);
app.UseRateLimiter();

// Health check propio del Gateway: no consulta a los servicios, solo dice que el Gateway está vivo.
// Es público (sin token) para que Docker y el equipo puedan verificarlo.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", servicio = "gateway" }));

// Activa el proxy: cada pedido se reenvía al servicio que corresponda según las rutas configuradas
app.MapReverseProxy();

app.Run();

static bool EsRutaDeCotizacionMagicLink(HttpRequest request)
    => MagicLinkGatewayRequest.EsCotizacion(request);

// Hace visible la clase Program para que los proyectos de pruebas puedan levantar el gateway.
public partial class Program { }
