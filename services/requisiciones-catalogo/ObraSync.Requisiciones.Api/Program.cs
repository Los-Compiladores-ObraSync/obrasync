using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ObraSync.Requisiciones.Application;
using ObraSync.Requisiciones.Infrastructure;
using ObraSync.Requisiciones.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Controladores
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger con soporte para JWT Bearer
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ObraSync.Requisiciones.Api",
        Version = "1.0",
        Description = "API del microservicio de Requisiciones y Catálogo de ObraSync Enterprise."
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
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Inyección de dependencias de Application e Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// Formato de errores estándar RFC 7807 (ProblemDetails) con propagación del CorrelationId de YARP
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

// Autenticación JWT (solo valida el token firmado por Usuarios)
var secret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    throw new InvalidOperationException(
        "El secreto JWT ('Jwt:Secret') no está configurado. Debe definirse por variables de entorno o configuración segura.");
}

// Emisor y audiencia esperados en el token (#136). Los emite Usuarios (JwtProvider); los 4 servicios los validan.
// Se pueden cambiar con las variables Jwt__Issuer y Jwt__Audience (tienen que ser iguales en todos los servicios).
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
    service = "requisiciones-catalogo",
    database = "DB_Req",
    timestamp = DateTime.UtcNow
}));

app.MapControllers();

// Aplicar migraciones automáticamente al arrancar cuando se utiliza un proveedor relacional (SQL Server)
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<RequisicionesDbContext>();
    if (context.Database.IsRelational())
    {
        context.Database.Migrate();
    }
    else
    {
        context.Database.EnsureCreated();
    }
}

app.Run();

// Clase parcial para permitir integración con WebApplicationFactory en proyectos de pruebas xUnit
public partial class Program { }
