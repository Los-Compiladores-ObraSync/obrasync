using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Recepcion.Infrastructure;
using ObraSync.Recepcion.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Servicios de controladores y Swagger (OpenAPI)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Persistencia y base de datos DB_Recepcion
builder.Services.AddInfrastructureServices(builder.Configuration);

// RabbitMQ + MassTransit (misma convención que Requisiciones y Licitaciones)
var useInMemoryTransport = string.Equals(
    builder.Configuration["RabbitMQ:UseInMemory"],
    "true",
    StringComparison.OrdinalIgnoreCase);

builder.Services.AddMassTransit(x =>
{
    if (useInMemoryTransport)
    {
        x.UsingInMemory((context, cfg) => cfg.ConfigureEndpoints(context));
    }
    else
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            var host = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            var virtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";
            var username = builder.Configuration["RabbitMQ:Username"] ?? "guest";
            var password = builder.Configuration["RabbitMQ:Password"] ?? "guest";

            cfg.Host(host, virtualHost, h =>
            {
                h.Username(username);
                h.Password(password);
            });

            cfg.ConfigureEndpoints(context);
        });
    }
});

// Formato de errores estándar RFC 7807 (ProblemDetails) con propagación del CorrelationId de YARP
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        var correlationId = ctx.HttpContext.Request.Headers["X-Correlation-ID"].ToString();
        if (!string.IsNullOrEmpty(correlationId))
        {
            ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

// Autenticación JWT: este microservicio SOLO valida el token emitido por el servicio de Usuarios
var secret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    throw new InvalidOperationException(
        "El secreto JWT ('Jwt:Secret') no está configurado. Debe definirse con la variable de entorno Jwt__Secret.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // Mantiene claims limpios (sub, email, name, role)
        options.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Entornos de desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// Endpoint de salud para Docker, Kubernetes y YARP Gateway
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "recepcion-presupuesto",
    database = "DB_Recepcion",
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

// Aplicar migraciones automáticamente al arrancar con reintentos acotados
if (!app.Environment.IsEnvironment("Testing"))
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        var context = services.GetRequiredService<RecepcionDbContext>();

        const int maxIntentos = 10;
        const int delaySegundos = 5;

        for (int intento = 1; intento <= maxIntentos; intento++)
        {
            try
            {
                logger.LogInformation("Intentando aplicar migraciones a la base de datos (intento {Intento}/{MaxIntentos})...", intento, maxIntentos);
                await context.Database.MigrateAsync();
                logger.LogInformation("Migraciones aplicadas exitosamente.");
                break;
            }
            catch (Exception ex)
            {
                if (ex is not Microsoft.Data.SqlClient.SqlException)
                {
                    throw;
                }

                logger.LogWarning(ex, "Error al intentar aplicar migraciones en el intento {Intento}/{MaxIntentos}.", intento, maxIntentos);
                if (intento == maxIntentos)
                {
                    logger.LogCritical(ex, "Se agotaron los {MaxIntentos} intentos para aplicar las migraciones. El servicio fallará.", maxIntentos);
                    throw;
                }

                await Task.Delay(TimeSpan.FromSeconds(delaySegundos));
            }
        }
    }
}

app.Run();

// Clase parcial para permitir integración con WebApplicationFactory en proyectos de pruebas xUnit
public partial class Program { }

