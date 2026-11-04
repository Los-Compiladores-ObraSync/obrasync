using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Services;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;
using ObraSync.Licitaciones.Application.UseCases.Licitaciones.Handlers;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using ObraSync.Licitaciones.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Errores en formato ProblemDetails + correlationId
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

builder.Services.AddDbContext<LicitacionesDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DbLicit")));

// Servicios, Repositorios y Handlers de Aplicación
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ILicitacionRepository, LicitacionRepository>();
builder.Services.AddScoped<ILicitacionService, LicitacionService>();
builder.Services.AddScoped<ICotizacionRepository, CotizacionRepository>();
builder.Services.AddScoped<GetLicitacionesQueryHandler>();
builder.Services.AddScoped<GetLicitacionByIdQueryHandler>();
builder.Services.AddScoped<CrearCotizacionCommandHandler>();
builder.Services.AddScoped<GetCotizacionesByLicitacionIdQueryHandler>();

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

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();

                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                httpContext.Response.ContentType = "application/problem+json";

                var correlationId = httpContext.Request.Headers["X-Correlation-ID"].ToString();
                if (string.IsNullOrWhiteSpace(correlationId))
                {
                    correlationId = httpContext.TraceIdentifier;
                }

                var problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                    Title = "No autorizado",
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = "Se requiere un token JWT válido para acceder a este recurso.",
                    Instance = httpContext.Request.Path
                };
                problemDetails.Extensions["correlationId"] = correlationId;

                var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
                if (problemDetailsService != null)
                {
                    await problemDetailsService.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        ProblemDetails = problemDetails
                    });
                }
                else
                {
                    await httpContext.Response.WriteAsJsonAsync(
                        problemDetails,
                        options: null,
                        contentType: "application/problem+json");
                }
            },
            OnForbidden = async context =>
            {
                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = "application/problem+json";

                var correlationId = httpContext.Request.Headers["X-Correlation-ID"].ToString();
                if (string.IsNullOrWhiteSpace(correlationId))
                {
                    correlationId = httpContext.TraceIdentifier;
                }

                var problemDetails = new ProblemDetails
                {
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                    Title = "Acceso denegado",
                    Status = StatusCodes.Status403Forbidden,
                    Detail = "Su rol no posee permisos suficientes para consultar o modificar este recurso.",
                    Instance = httpContext.Request.Path
                };
                problemDetails.Extensions["correlationId"] = correlationId;

                var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
                if (problemDetailsService != null)
                {
                    await problemDetailsService.WriteAsync(new ProblemDetailsContext
                    {
                        HttpContext = httpContext,
                        ProblemDetails = problemDetails
                    });
                }
                else
                {
                    await httpContext.Response.WriteAsJsonAsync(
                        problemDetails,
                        options: null,
                        contentType: "application/problem+json");
                }
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Aplica las migraciones al arrancar (en Docker la base nace vacía).
// Solo si hay cadena de conexión, para no romper las pruebas del /health.
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DbLicit")))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
    if (db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "licitaciones" }));
app.MapControllers();

app.Run();

public partial class Program { } // para WebApplicationFactory en las pruebas
