using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Api.Hubs;
using ObraSync.Licitaciones.Api.Middlewares;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Services;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using ObraSync.Licitaciones.Infrastructure.Messaging;
using ObraSync.Licitaciones.Infrastructure.Repositories;

const string chatHubPath = "/api/ordenes-compra/chat-hub";
const string signalRAccessTokenItemKey = "ObraSync.SignalR.AccessToken";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient("RecepcionPresupuesto", client =>
{
    var url = builder.Configuration["RecepcionPresupuesto:BaseUrl"]
        ?? "http://localhost:5004";

    client.BaseAddress = new Uri(url);
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddScoped<
    ObraSync.Licitaciones.Application.Interfaces.IEstadoPresupuestarioOrden,
    EstadoPresupuestarioOrdenClient>();
builder.Services.AddSignalR();

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
builder.Services.AddExceptionProblemMapping();

builder.Services.AddDbContext<LicitacionesDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DbLicit")));

// Registro de MediatR para la capa de Aplicación
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CrearCotizacionCommandHandler).Assembly));

// Servicios y Repositorios de Aplicación
builder.Services.AddScoped(sp => sp.GetRequiredService<IConfiguration>().ReadAndValidateScoringOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ILicitacionRepository, LicitacionRepository>();
builder.Services.AddScoped<ILicitacionService, LicitacionService>();
builder.Services.AddScoped<ICotizacionRepository, CotizacionRepository>();
builder.Services.AddScoped<ICotizacionValidadorService, CotizacionValidadorService>();
builder.Services.AddScoped<IOrdenCompraRepository, OrdenCompraRepository>();
builder.Services.AddScoped<IMagicLinkRepository, MagicLinkRepository>();
builder.Services.AddScoped<ICalificacionProveedorRepository, CalificacionProveedorRepository>();
builder.Services.AddScoped<IChatOrdenRepository, ChatOrdenRepository>();
builder.Services.AddScoped<IChatOrdenNotifier, SignalRChatOrdenNotifier>();
builder.Services.AddScoped<ILicitacionEventPublisher, MassTransitLicitacionEventPublisher>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// RabbitMQ + MassTransit (misma convención que Requisiciones)
var useInMemoryTransport = string.Equals(
    builder.Configuration["RabbitMQ:UseInMemory"],
    "true",
    StringComparison.OrdinalIgnoreCase);

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RequisicionAprobadaConsumer>();
    x.AddConsumer<RecepcionConfirmadaConsumer>();
    x.AddConsumer<RecepcionConProblemaConsumer>();

    if (useInMemoryTransport)
    {
        x.UsingInMemory((context, cfg) =>
        {
            cfg.UsarDecimalesComoNumero(); // decimales como número en los eventos (#128)
            cfg.ConfigureEndpoints(context);
        });
    }
    else
    {
        var username = builder.Configuration["RabbitMQ:Username"];
        var password = builder.Configuration["RabbitMQ:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("RabbitMQ Username y Password deben estar configurados cuando no se utiliza el transporte en memoria (#169). Configure 'RabbitMQ:Username' y 'RabbitMQ:Password'.");
        }

        // Outbox transaccional (#135): el evento se guarda en DB_Licit junto con la adjudicación
        // y un servicio en segundo plano lo entrega a RabbitMQ. Solo con el transporte real.
        x.AddEntityFrameworkOutbox<LicitacionesDbContext>(outbox =>
        {
            outbox.UseSqlServer();
            outbox.UseBusOutbox();
        });

        x.UsingRabbitMq((context, cfg) =>
        {
            var host = builder.Configuration["RabbitMQ:Host"] ?? "localhost";
            var virtualHost = builder.Configuration["RabbitMQ:VirtualHost"] ?? "/";

            cfg.Host(host, virtualHost, h =>
            {
                h.Username(username);
                h.Password(password);
            });

            cfg.UsarDecimalesComoNumero(); // decimales como número en los eventos (#128)
            cfg.UsarReintentosExponenciales(); // errores pasajeros: reintentar antes de mandar a _error (#179)
            cfg.ConfigureEndpoints(context);
        });
    }
});

// Solo VALIDAMOS el token, no lo emitimos (eso lo hace Usuarios)
var secret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(secret))
{
    throw new InvalidOperationException(
        "El secreto JWT ('Jwt:Secret') no está configurado. Debe definirse con la variable de entorno Jwt__Secret.");
}

// Emisor y audiencia esperados en el token (#136). Los emite Usuarios (JwtProvider); los 4 servicios los validan.
// Se pueden cambiar con las variables Jwt__Issuer y Jwt__Audience (tienen que ser iguales en todos los servicios).
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "obrasync-usuarios";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "obrasync-services";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // mantiene los nombres sub, email, name, role
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
            OnMessageReceived = context =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments(chatHubPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (context.HttpContext.Items.TryGetValue(signalRAccessTokenItemKey, out var tokenObj) &&
                        tokenObj is string token &&
                        !string.IsNullOrWhiteSpace(token))
                    {
                        context.Token = token;
                    }
                    else
                    {
                        var queryToken = context.Request.Query["access_token"].ToString();
                        if (!string.IsNullOrWhiteSpace(queryToken))
                        {
                            context.Token = queryToken;
                        }
                    }
                }

                return Task.CompletedTask;
            },
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "ObraSync.Licitaciones.Api", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingresá el token JWT obtenido en el login."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Validar configuración de pesos de Scoring al arrancar (#197)
app.Configuration.ReadAndValidateScoringOptions();

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

// Seguridad (#218): en SignalR el token viaja por query (?access_token=...) y podría exponerse en logs
// si alguien eleva el nivel de log. Lo movemos a HttpContext.Items y lo removemos de Request.QueryString
// antes de correlación, routing, logging o manejo de errores.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments(chatHubPath, StringComparison.OrdinalIgnoreCase) &&
        context.Request.Query.TryGetValue("access_token", out var tokenValues))
    {
        var token = tokenValues.ToString();
        if (!string.IsNullOrWhiteSpace(token))
        {
            context.Items[signalRAccessTokenItemKey] = token;
        }

        var parametrosRestantes = context.Request.Query
            .Where(kv => !string.Equals(kv.Key, "access_token", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v)));

        context.Request.QueryString = QueryString.Create(parametrosRestantes);
    }

    await next();
});

app.Use(async (context, next) =>
{
    const string headerName = "X-Correlation-ID";
    var correlationId = context.Request.Headers[headerName].FirstOrDefault();

    if (string.IsNullOrWhiteSpace(correlationId))
    {
        correlationId = Guid.NewGuid().ToString();
        context.Request.Headers[headerName] = correlationId;
    }

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers[headerName] = correlationId;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[headerName] = correlationId;
        return Task.CompletedTask;
    });

    var loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
    var logger = loggerFactory?.CreateLogger("ObraSync.Licitaciones.CorrelationId");
    using (logger?.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next();
    }
});

app.UseMiddleware<ExceptionMiddleware>();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "licitaciones" }));
app.MapControllers();
app.MapHub<ChatOrdenHub>(chatHubPath);

app.Run();

public partial class Program { } // para WebApplicationFactory en las pruebas