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

public class PreciosHistoricosEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PreciosHistoricosEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Precios_{Guid.NewGuid()}";

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

    private static OrdenCompra Orden(Guid articuloId, decimal precio, DateTime emision) => new()
    {
        OrdenCompraId = Guid.NewGuid(),
        LicitacionId = Guid.NewGuid(),
        CotizacionId = Guid.NewGuid(),
        ProveedorId = Guid.NewGuid(),
        Estado = "Emitida",
        FechaEmision = emision,
        Items = new List<OrdenCompraItem>
        {
            new() { Id = Guid.NewGuid(), ArticuloId = articuloId, Descripcion = "Cemento", Cantidad = 10m, UnidadMedida = "BOLSA", PrecioUnitario = precio }
        }
    };

    private static HttpClient Cliente(WebApplicationFactory<Program> factory, string rol)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));
        return client;
    }

    [Fact]
    public async Task PreciosHistoricos_DevuelveElDeLaOrdenMasRecienteDeCadaArticulo_YOmiteLosQueNuncaSeCompraron()
    {
        var cemento = Guid.NewGuid();
        var arena = Guid.NewGuid();
        var nuncaComprado = Guid.NewGuid();
        using var factory = CreateIsolatedFactory(db =>
        {
            db.OrdenesCompra.Add(Orden(cemento, 100m, DateTime.UtcNow.AddDays(-30)));
            db.OrdenesCompra.Add(Orden(cemento, 120m, DateTime.UtcNow.AddDays(-5)));  // la más nueva
            db.OrdenesCompra.Add(Orden(arena, 50m, DateTime.UtcNow.AddDays(-10)));
        });

        var resp = await Cliente(factory, "Comprador").GetAsync(
            $"/api/ordenes-compra/precios-historicos?articuloIds={cemento}&articuloIds={arena}&articuloIds={nuncaComprado}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var lista = await resp.Content.ReadFromJsonAsync<List<PrecioHistoricoDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(lista);
        Assert.Equal(2, lista.Count);
        Assert.Equal(120m, lista.Single(x => x.ArticuloId == cemento).UltimoPrecioUnitario);
        Assert.Equal(50m, lista.Single(x => x.ArticuloId == arena).UltimoPrecioUnitario);
        Assert.DoesNotContain(lista, x => x.ArticuloId == nuncaComprado);
    }

    [Fact]
    public async Task PreciosHistoricos_SinArticulos_Devuelve400()
    {
        using var factory = CreateIsolatedFactory();

        var resp = await Cliente(factory, "Comprador").GetAsync("/api/ordenes-compra/precios-historicos");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Proveedor")]
    public async Task PreciosHistoricos_ConRolNoAutorizado_Devuelve403(string rol)
    {
        using var factory = CreateIsolatedFactory();

        var resp = await Cliente(factory, rol).GetAsync($"/api/ordenes-compra/precios-historicos?articuloIds={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
