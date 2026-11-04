using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Infrastructure;

namespace ObraSync.Licitaciones.Tests;

public class LicitacionesApiFactory : WebApplicationFactory<Program>
{
    public const string Secret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly string _dbName = Guid.NewGuid().ToString();

    public LicitacionesApiFactory()
    {
        // Program.cs lee estas claves al arrancar: secreto del JWT y bus en memoria (sin RabbitMQ)
        Environment.SetEnvironmentVariable("Jwt__Secret", Secret);
        Environment.SetEnvironmentVariable("RabbitMQ__UseInMemory", "true");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Base en memoria en lugar de SQL Server
            services.RemoveAll<DbContextOptions<LicitacionesDbContext>>();
            services.AddDbContext<LicitacionesDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }

    public static string CrearToken(string rol)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var token = new JwtSecurityToken(
            claims: new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim("email", $"{rol.ToLower()}@obrasync.test"),
                new Claim("name", rol),
                new Claim("role", rol)
            },
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}