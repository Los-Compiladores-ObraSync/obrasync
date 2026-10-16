using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Recepcion.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Servicios de controladores y Swagger (OpenAPI)
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Persistencia y base de datos DB_Recepcion
builder.Services.AddInfrastructureServices(builder.Configuration);

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
var secret = builder.Configuration["Jwt:Secret"]
    ?? "ObraSyncSecretKeySprint1SuperSegura2026!";

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

app.Run();

// Clase parcial para permitir integración con WebApplicationFactory en proyectos de pruebas xUnit
public partial class Program { }
