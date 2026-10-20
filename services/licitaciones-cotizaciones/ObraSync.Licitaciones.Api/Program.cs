using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Errores en formato ProblemDetails + correlationId
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["correlationId"] =
            ctx.HttpContext.Request.Headers["X-Correlation-ID"].ToString();
    };
});

builder.Services.AddDbContext<LicitacionesDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DbLicit")));

// RabbitMQ + MassTransit (misma convención que Requisiciones)
var useInMemoryTransport = string.Equals(
    builder.Configuration["RabbitMQ:UseInMemory"],
    "true",
    StringComparison.OrdinalIgnoreCase);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RequisicionAprobadaConsumer>();

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

// Solo VALIDAMOS el token, no lo emitimos (eso lo hace Usuarios)
var secret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Falta la variable de entorno Jwt__Secret");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // mantiene los nombres sub, email, name, role
        options.TokenValidationParameters = new TokenValidationParameters
        {
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = false,   // provisorio: ajustar cuando el contrato #16 lo defina
            ValidateAudience = false,
            ValidateLifetime = true,
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Aplica las migraciones al arrancar (en Docker la base nace vacía).
// Solo si hay cadena de conexión, para no romper las pruebas del /health.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DbLicit")))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>().Database.Migrate();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "licitaciones" }));
app.MapControllers();

app.Run();

public partial class Program { } // para WebApplicationFactory en las pruebas