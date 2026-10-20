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
    .ValidateOnStart();

// 4. Inyección de dependencias de servicios
builder.Services.AddScoped<IJwtProvider, JwtProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

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