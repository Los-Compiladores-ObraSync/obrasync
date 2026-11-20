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

public class CorregirCotizacionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public CorregirCotizacionEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Corregir_{Guid.NewGuid()}";

        var factory = _baseFactory.WithWebHostBuilder(builder =>
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

        // El sembrado corre al armar el host (ConfigureServices): se fuerza acá para que los datos existan
        // antes de que el test los lea.
        _ = factory.Server;
        return factory;
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

    private sealed record Escenario(Guid LicitacionId, Guid CotizacionId, Guid ProveedorId, Guid ArticuloId);

    private static Escenario Sembrar(
        LicitacionesDbContext db,
        EstadoLicitacion estado = EstadoLicitacion.Abierta,
        TimeSpan? vence = null)
    {
        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0700",
            ObraId = Guid.NewGuid(),
            Estado = estado,
            FechaCreacion = DateTime.UtcNow.AddHours(-3),
            FechaLimiteCotizacion = DateTime.UtcNow.Add(vence ?? TimeSpan.FromDays(4)),
            Items = new List<LicitacionItem>
            {
                new() { Id = Guid.NewGuid(), LicitacionId = licitacionId, ArticuloId = articuloId, Descripcion = "Cemento", Cantidad = 50m, UnidadMedida = "BOLSA" }
            }
        });
        db.Cotizaciones.Add(new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            ProveedorNombre = "Corralón Sur",
            Flete = 1000m,
            PlazoEntregaDias = 5,
            FechaCreacion = DateTime.UtcNow.AddHours(-1),
            Items = new List<CotizacionItem>
            {
                new() { Id = Guid.NewGuid(), CotizacionId = cotizacionId, ArticuloId = articuloId, PrecioUnitario = 9000m }
            }
        });
        return new Escenario(licitacionId, cotizacionId, proveedorId, articuloId);
    }

    private static object Cuerpo(Escenario e, decimal precio, decimal flete = 500m, int plazo = 3) => new
    {
        licitacionId = e.LicitacionId,
        proveedorId = e.ProveedorId,
        flete,
        plazoEntregaDias = plazo,
        items = new[] { new { articuloId = e.ArticuloId, precioUnitario = precio } }
    };

    private static HttpClient Cliente(WebApplicationFactory<Program> factory, string rol, Guid? usuarioId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol, usuarioId));
        return client;
    }

    [Fact]
    public async Task Corregir_ElProveedorDuenoCambiaPrecioFleteYPlazo_ConservaElMismoId()
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db));

        var resp = await Cliente(factory, "Proveedor", e!.ProveedorId)
            .PutAsJsonAsync($"/api/cotizaciones/{e.CotizacionId}", Cuerpo(e, precio: 8500m, flete: 400m, plazo: 2));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var guardada = await db2.Cotizaciones.Include(c => c.Items).SingleAsync(c => c.CotizacionId == e.CotizacionId);
        Assert.Equal(400m, guardada.Flete);
        Assert.Equal(2, guardada.PlazoEntregaDias);
        Assert.Equal(8500m, Assert.Single(guardada.Items).PrecioUnitario);
        Assert.Equal(1, await db2.Cotizaciones.CountAsync(c => c.LicitacionId == e.LicitacionId));
    }

    [Fact]
    public async Task Corregir_OtroProveedor_Devuelve403_YNoCambiaNada()
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db));

        var resp = await Cliente(factory, "Proveedor", Guid.NewGuid())
            .PutAsJsonAsync($"/api/cotizaciones/{e!.CotizacionId}", Cuerpo(e, precio: 1m));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db2 = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var intacta = await db2.Cotizaciones.Include(c => c.Items).SingleAsync(c => c.CotizacionId == e.CotizacionId);
        Assert.Equal(9000m, Assert.Single(intacta.Items).PrecioUnitario);
    }

    [Theory]
    [InlineData(EstadoLicitacion.Cerrada)]
    [InlineData(EstadoLicitacion.Adjudicada)]
    public async Task Corregir_LicitacionNoAbierta_Devuelve400(EstadoLicitacion estado)
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db, estado));

        var resp = await Cliente(factory, "Proveedor", e!.ProveedorId)
            .PutAsJsonAsync($"/api/cotizaciones/{e.CotizacionId}", Cuerpo(e, precio: 8000m));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Corregir_ConElPlazoVencido_Devuelve400()
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db, vence: TimeSpan.FromMinutes(-5)));

        var resp = await Cliente(factory, "Proveedor", e!.ProveedorId)
            .PutAsJsonAsync($"/api/cotizaciones/{e.CotizacionId}", Cuerpo(e, precio: 8000m));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Corregir_CotizacionInexistente_Devuelve404()
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db));

        var resp = await Cliente(factory, "Proveedor", e!.ProveedorId)
            .PutAsJsonAsync($"/api/cotizaciones/{Guid.NewGuid()}", Cuerpo(e, precio: 8000m));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Comprador")]
    public async Task Corregir_ConRolNoAutorizado_Devuelve403(string rol)
    {
        Escenario? e = null;
        using var factory = CreateIsolatedFactory(db => e = Sembrar(db));

        var resp = await Cliente(factory, rol)
            .PutAsJsonAsync($"/api/cotizaciones/{e!.CotizacionId}", Cuerpo(e, precio: 8000m));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
