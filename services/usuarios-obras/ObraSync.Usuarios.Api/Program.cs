using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ObraSync.Usuarios.Api.Extensions;
using ObraSync.Usuarios.Api.Middlewares;
using ObraSync.Usuarios.Application.Interfaces;
using ObraSync.Usuarios.Application.Services;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using ObraSync.Usuarios.Infrastructure.Services;
using ObraSync.Usuarios.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurar DbContext con SQL Server (DB_Auth)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null);
    }));

// 2. Configurar ASP.NET Core Identity usando Guid como PK
builder.Services.AddIdentity<AppUser, AppRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AuthDbContext>()
.AddDefaultTokenProviders();

// 3. Opciones tipadas de JwtSettings con validación al inicio de la aplicación
builder.Services
    .AddOptions<JwtSettings>()
    .BindConfiguration(JwtSettings.SectionName)
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Secret),
    "La clave secreta JWT ('Jwt__Secret') no está configurada en las variables de entorno.")
    .ValidateOnStart();

// 4. Inyección de dependencias de servicios
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();

// 1. Obtención y validación de la clave secreta JWT
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>();
var jwtSecret = jwtSettings?.Secret
    ?? builder.Configuration["Jwt__Secret"]
    ?? builder.Configuration["Jwt:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    throw new InvalidOperationException("La variable de entorno 'Jwt__Secret' no está configurada.");
}

// 2. Registro del servicio de autenticación JWT Bearer
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.MapInboundClaims = false; // Desactiva la conversión automática de claims a URIs de Microsoft
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        NameClaimType = "name",
        RoleClaimType = "role"
    };
});

builder.Services.AddControllers();

// <-- NUEVO: Registra el servicio para estandarizar errores vacíos a ProblemDetails
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "ObraSync.Usuarios.Api", Version = "v1" });

    // Configura el soporte para JWT Bearer y hace aparecer el botón Authorize
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

// <-- Intercepta códigos de error HTTP (como 401 y 403) y les inyecta el ProblemDetails
app.UseStatusCodePages();

// 5. Registrar Middleware de Excepciones al principio del pipeline
app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// 6. Sembrado de base de datos y roles al arrancar
await app.SeedDatabaseAsync();

app.Run();