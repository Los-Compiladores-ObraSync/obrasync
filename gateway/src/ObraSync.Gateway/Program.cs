using ObraSync.Gateway;

var builder = WebApplication.CreateBuilder(args);

// YARP: carga las rutas y los destinos desde la sección "ReverseProxy" de appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Tiene que ir ANTES de MapReverseProxy: así el encabezado ya existe cuando YARP reenvía el pedido.
app.UseMiddleware<CorrelationIdMiddleware>();

// Si un servicio no responde, YARP devuelve un 502/504 vacío: acá se completa con un ProblemDetails.
// Va después del Correlation ID (para incluirlo) y antes del proxy.
app.Use(ErroresDelProxy.ResponderConProblemDetailsAsync);

// Health check propio del Gateway: no consulta a los servicios, solo dice que el Gateway está vivo.
// Es público (sin token) para que Docker y el equipo puedan verificarlo.
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", servicio = "gateway" }));

// Activa el proxy: cada pedido se reenvía al servicio que corresponda según las rutas configuradas
app.MapReverseProxy();

app.Run();

// Hace visible la clase Program para que los proyectos de pruebas puedan levantar el gateway.
public partial class Program { }