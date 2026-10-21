using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Recepcion.Infrastructure.Persistence;

namespace ObraSync.Recepcion.Tests;

public class RecepcionApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres-segura!";
    public string DatabaseName { get; } = $"DB_Recepcion_Test_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", TestJwtSecret);
        builder.UseSetting("ConnectionStrings:DefaultConnection", "Server=localhost,1433;Database=DB_Test;User Id=sa;Password=no-usada;TrustServerCertificate=True;");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<RecepcionDbContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<RecepcionDbContext>(options =>
            {
                options.UseInMemoryDatabase(DatabaseName);
            });
        });
    }

    public static string CreateToken(string role = "Capataz", Guid? userId = null, bool expired = false, string? customSecret = null)
    {
        var secret = customSecret ?? TestJwtSecret;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", "test@obrasync.com"),
            new Claim("name", "Usuario Test"),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expired ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
