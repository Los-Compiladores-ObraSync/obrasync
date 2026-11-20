using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.MagicLinks;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class ExtenderLicitacionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ExtenderLicitacionEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Extender_{Guid.NewGuid()}";

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();

                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<LicitacionesDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IDbContextOptionsConfiguration<LicitacionesDbContext>>();

                services.AddDbContext<LicitacionesDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });

                if (seedAction != null)
                {
                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
                    seedAction(db);
                    db.SaveChanges();
                }
            });
        });
    }

    private static string GenerarTokenJwt(string role, Guid? userId = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", $"{role.ToLowerInvariant()}@obrasync.com"),
            new Claim("name", $"Usuario {role}"),
            new Claim("role", role)
        };

        var token = new JwtSecurityToken(
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Licitacion CrearLicitacion(
        EstadoLicitacion estado = EstadoLicitacion.Abierta,
        Guid? licitacionId = null,
        params Guid[] articulosIds)
    {
        var id = licitacionId ?? Guid.NewGuid();
        var ids = articulosIds.Length > 0 ? articulosIds : new[] { Guid.NewGuid() };

        return new Licitacion
        {
            LicitacionId = id,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = Guid.NewGuid(),
            Estado = estado,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = ids.Select((artId, index) => new LicitacionItem
            {
                Id = Guid.NewGuid(),
                LicitacionId = id,
                ArticuloId = artId,
                Descripcion = $"Material {index + 1}",
                Cantidad = 10m,
                UnidadMedida = "BOLSA"
            }).ToList()
        };
    }

    private static async Task AssertProblemDetailsWithCorrelationIdAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCorrelationId)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal((int)expectedStatus, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCorrelationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    private static async Task<HttpResponseMessage> Extender(HttpClient client, Guid id, DateTime nueva) =>
        await client.PostAsJsonAsync($"/api/licitaciones/{id}/extender", new { fechaLimiteCotizacion = nueva });

    private static HttpClient ClienteDe(WebApplicationFactory<Program> factory, string rol)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));
        return client;
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task Extender_LicitacionAbiertaConFechaPosterior_GuardaLaNuevaFecha(string rol)
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));
        var nueva = DateTime.UtcNow.AddDays(9);

        var resp = await Extender(ClienteDe(factory, rol), licitacion.LicitacionId, nueva);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var guardada = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(nueva, guardada.FechaLimiteCotizacion, TimeSpan.FromSeconds(1));
        Assert.Equal(EstadoLicitacion.Abierta, guardada.Estado);
    }

    [Fact]
    public async Task Extender_ConFechaAnteriorALaActual_Devuelve400_YNoCambiaNada()
    {
        var licitacion = CrearLicitacion(); // vence en 5 días
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var resp = await Extender(ClienteDe(factory, "Comprador"), licitacion.LicitacionId, DateTime.UtcNow.AddDays(2));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Extender_ConFechaPasada_Devuelve400()
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var resp = await Extender(ClienteDe(factory, "Comprador"), licitacion.LicitacionId, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData(EstadoLicitacion.Cerrada)]
    [InlineData(EstadoLicitacion.Adjudicada)]
    public async Task Extender_LicitacionNoAbierta_Devuelve409(EstadoLicitacion estado)
    {
        var licitacion = CrearLicitacion(estado);
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var resp = await Extender(ClienteDe(factory, "Comprador"), licitacion.LicitacionId, DateTime.UtcNow.AddDays(9));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Extender_LicitacionInexistente_Devuelve404()
    {
        using var factory = CreateIsolatedFactory();

        var resp = await Extender(ClienteDe(factory, "Comprador"), Guid.NewGuid(), DateTime.UtcNow.AddDays(9));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Theory]
    [InlineData("Proveedor")]
    [InlineData("Capataz")]
    public async Task Extender_ConRolNoAutorizado_Devuelve403(string rol)
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var resp = await Extender(ClienteDe(factory, rol), licitacion.LicitacionId, DateTime.UtcNow.AddDays(9));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
