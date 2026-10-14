using ObraSync.Gateway;

var builder = WebApplication.CreateBuilder(args);

// YARP: carga las rutas y los destinos desde la sección "ReverseProxy" de appsettings.json
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

// Tiene que ir ANTES de MapReverseProxy: así el encabezado ya existe cuando YARP reenvía el pedido.
app.UseMiddleware<CorrelationIdMiddleware>();

// Activa el proxy: cada pedido se reenvía al servicio que corresponda según las rutas configuradas
app.MapReverseProxy();

app.Run();

// Hace visible la clase Program para que los proyectos de pruebas puedan levantar el gateway.
public partial class Program { }
