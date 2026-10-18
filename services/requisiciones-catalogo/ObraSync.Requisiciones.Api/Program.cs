using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ObraSync.Requisiciones.Application;
using ObraSync.Requisiciones.Infrastructure;

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
        if (!string.IsNullOrEmpty(correlationId))
        {
            ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
        }
    };
});

// Autenticación JWT (solo valida el token firmado por Usuarios)
var secret = builder.Configuration["Jwt:Secret"]
    ?? "ObraSyncSecretKeySprint1SuperSegura2026!";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
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

app.Run();

// Clase parcial para permitir integración con WebApplicationFactory en proyectos de pruebas xUnit
public partial class Program { }
