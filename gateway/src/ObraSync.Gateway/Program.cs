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
var authRateLimit = builder.Configuration.GetSection("AuthRateLimit").Get<AuthRateLimitOptions>()
    ?? new AuthRateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Cada grupo de rutas públicas tiene su propia ventana por IP (la clave lleva el prefijo del grupo).
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ip = (httpContext.Connection.RemoteIpAddress ?? IPAddress.None).ToString();

        if (MagicLinkGatewayRequest.EsMagicLink(httpContext.Request))
            return Ventana("magic:" + ip, magicLinkRateLimit.PermitLimit, magicLinkRateLimit.WindowSeconds, magicLinkRateLimit.SegmentsPerWindow);

        // Login, registro y recuperación de contraseña: frenan la fuerza bruta y el abuso de correos.
        if (AutenticacionPublicaGatewayRequest.Es(httpContext.Request))
            return Ventana("auth:" + ip, authRateLimit.PermitLimit, authRateLimit.WindowSeconds, authRateLimit.SegmentsPerWindow);

        return RateLimitPartition.GetNoLimiter("sin-limite");
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

static RateLimitPartition<string> Ventana(string clave, int permisos, int segundos, int segmentos) =>
    RateLimitPartition.GetSlidingWindowLimiter(clave, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = permisos,
        Window = TimeSpan.FromSeconds(segundos),
        SegmentsPerWindow = segmentos,
        QueueLimit = 0,
        AutoReplenishment = true
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

// El limitador solo se ejecuta para las operaciones del Magic Link (cotizar y ver el pliego; comparten la ventana por IP); no afecta las demás rutas.
// Auditoría sin datos sensibles: no se incluye el segmento {hash}, query string, URL ni cuerpo.
app.Use(async (context, next) =>
{
    if (!MagicLinkGatewayRequest.EsMagicLink(context.Request))
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
            MagicLinkGatewayRequest.EsPliego(context.Request)
                ? "/api/licitaciones/magic-links/{hash}"
                : "/api/licitaciones/magic-links/{hash}/cotizar",
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

// Hace visible la clase Program para que los proyectos de pruebas puedan levantar el gateway.
public partial class Program { }
