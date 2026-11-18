using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ObraSync.Asistente.Api.Mcp;
using ObraSync.Asistente.Api.Servicios;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ObraSync.Asistente.Api",
        Version = "1.0",
        Description = "Chat del asistente de IA de ObraSync Enterprise. El asistente nunca escribe datos."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Token JWT emitido por ObraSync.Usuarios.Api (HS256). Ingrese 'Bearer {token}'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// Application: el agente y su herramienta (uno por pedido, porque dependen del JWT del usuario que escribe).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IContextoDePeticion, ContextoDePeticionHttp>();
builder.Services.AddScoped<HerramientasCatalogo>();
builder.Services.AddScoped<HerramientasPresupuestos>();
builder.Services.AddScoped<HerramientasCompras>();
builder.Services.AddScoped<HerramientasRecepcion>();
builder.Services.AddScoped<HerramientasLicitaciones>();
builder.Services.AddScoped<Agente>();
// Lectura de remitos por foto (#209): usa el mismo control de consumo que el chat (el cupo es de todo el grupo).
builder.Services.AddScoped<ServicioLecturaRemito>(sp => new ServicioLecturaRemito(
    sp.GetRequiredService<IExtractorRemito>(), sp.GetRequiredService<IControlConsumo>()));

// Control de consumo de tokens (#144): UNA sola instancia para todo el servicio, porque el cupo del modelo es
// compartido por todos los usuarios. Los topes y el interruptor se leen de la sección "Consumo" (Consumo__Habilitado, etc.).
var opcionesConsumo = builder.Configuration.GetSection("Consumo").Get<OpcionesConsumo>() ?? new OpcionesConsumo();
builder.Services.AddSingleton<IControlConsumo>(sp => new ControlConsumoEnMemoria(
    opcionesConsumo, sp.GetRequiredService<ILogger<ControlConsumoEnMemoria>>()));

// Límite por usuario (#144): cada persona puede mandar N mensajes por hora al chat (20 por defecto), para que una sola
// no se coma el cupo de todo el grupo. Usa el limitador que ya trae ASP.NET Core 8 (sin paquetes nuevos).
// Se aplica DESPUÉS de autenticar, así que la identidad ("sub") ya está validada con la firma del JWT.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat-por-usuario", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.FindFirst("sub")?.Value ?? "sin-usuario",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = opcionesConsumo.MensajesPorHoraPorUsuario,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
    // Cada foto gasta bastante más que un mensaje: tiene su propio tope por usuario (10 por hora por defecto).
    options.AddPolicy("remito-por-usuario", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: httpContext.User.FindFirst("sub")?.Value ?? "sin-usuario",
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = opcionesConsumo.FotosPorHoraPorUsuario,
                Window = TimeSpan.FromHours(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0
            }));
    options.OnRejected = async (contexto, ct) =>
    {
        var espera = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? (int)Math.Ceiling(retry.TotalSeconds)
            : 60;
        contexto.HttpContext.Response.Headers.RetryAfter = espera.ToString();
        await EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status429TooManyRequests,
            "https://tools.ietf.org/html/rfc6585#section-4", "Límite de mensajes alcanzado",
            "Mandaste demasiados mensajes al asistente en esta hora. Probá de nuevo más tarde.");
    };
});

// Infrastructure: cliente del gateway de IA y buscador del catálogo (vía API Gateway).
builder.Services.AddAsistenteInfrastructure(builder.Configuration);

// Servidor MCP (#98): expone las MISMAS herramientas (solo lectura) a asistentes de IA externos, por HTTP.
// Stateless: cada pedido es independiente y no se guardan sesiones (las herramientas no necesitan que el servidor
// le pregunte nada al cliente). El JWT del cliente viaja en cada pedido y se usa para consultar al Gateway.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithRequestFilters(FiltroHerramientasMcpPorRuta.Configurar)
    .WithTools<HerramientasMcp>();

// Errores en formato RFC 7807 (ProblemDetails) con el correlationId que propaga el Gateway
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        var correlationId = ctx.HttpContext.Request.Headers["X-Correlation-ID"].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = ctx.HttpContext.TraceIdentifier;
        }

        ctx.ProblemDetails.Instance ??= ctx.HttpContext.Request.Path;
        ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
    };
});

// Autenticación JWT: este servicio solo VALIDA el token firmado por Usuarios (mismo secreto, emisor y audiencia).
var secret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    throw new InvalidOperationException(
        "El secreto JWT ('Jwt:Secret') no está configurado. Debe definirse por variables de entorno o configuración segura.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "obrasync-usuarios";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "obrasync-services";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "role"
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await EscribirProblemaAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                    "https://tools.ietf.org/html/rfc9110#section-15.5.2", "No autorizado",
                    "Se requiere un token JWT válido para acceder a este recurso.");
            },
            OnForbidden = async context =>
            {
                await EscribirProblemaAsync(context.HttpContext, StatusCodes.Status403Forbidden,
                    "https://tools.ietf.org/html/rfc9110#section-15.5.4", "Acceso denegado",
                    "Su rol no tiene acceso al asistente.");
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Endpoint de salud para Docker y el Gateway. No muestra la clave: solo si el asistente está configurado.
app.MapGet("/health", (ObraSync.Asistente.Infrastructure.ConfiguracionIA ia) => Results.Ok(new
{
    status = "ok",
    service = "asistente",
    iaConfigurada = ia.EstaActivo,
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

// Las herramientas MCP existentes conservan su autorización original.
app.MapMcp("/api/mcp").RequireAuthorization(new AuthorizeAttribute { Roles = "Capataz,Admin" });

// Ruta presupuestaria separada: el filtro limita tools/list y tools/call a la consulta de partidas.
app.MapMcp(FiltroHerramientasMcpPorRuta.RutaPresupuestos)
    .RequireAuthorization(new AuthorizeAttribute { Roles = "Capataz,Comprador,Admin" });

app.Run();

static async Task EscribirProblemaAsync(HttpContext httpContext, int estado, string tipo, string titulo, string detalle)
{
    httpContext.Response.StatusCode = estado;
    httpContext.Response.ContentType = "application/problem+json";

    var correlationId = httpContext.Request.Headers["X-Correlation-ID"].ToString();
    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = httpContext.TraceIdentifier;
    }

    var problema = new ProblemDetails
    {
        Type = tipo,
        Title = titulo,
        Status = estado,
        Detail = detalle,
        Instance = httpContext.Request.Path
    };
    problema.Extensions["correlationId"] = correlationId;

    await httpContext.Response.WriteAsJsonAsync(problema, options: null, contentType: "application/problem+json");
}

// Clase parcial para permitir integración con WebApplicationFactory en el proyecto de pruebas xUnit
public partial class Program { }
