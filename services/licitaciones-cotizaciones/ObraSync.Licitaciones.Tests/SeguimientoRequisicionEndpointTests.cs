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

public class SeguimientoRequisicionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public SeguimientoRequisicionEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Seguimiento_{Guid.NewGuid()}";

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

    private static async Task<SeguimientoRequisicionDto?> Consultar(HttpClient client, Guid requisicionId)
    {
        var resp = await client.GetAsync($"/api/licitaciones/seguimiento/{requisicionId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<SeguimientoRequisicionDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    [Fact]
    public async Task Seguimiento_SinLicitacion_Devuelve200ConCamposVacios()
    {
        using var factory = CreateIsolatedFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));

        var requisicionId = Guid.NewGuid();
        var dto = await Consultar(client, requisicionId);

        Assert.NotNull(dto);
        Assert.Equal(requisicionId, dto.RequisicionId);
        Assert.Null(dto.LicitacionId);
        Assert.Equal(0, dto.CantidadCotizaciones);
        Assert.Empty(dto.OrdenesCompra);
    }

    [Fact]
    public async Task Seguimiento_ConLicitacionOfertasYOrden_LoDevuelveCompleto_ParaCapatazCompradorYAdmin()
    {
        var requisicionId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var cotizacionId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = requisicionId,
                NumeroRequisicion = "REQ-2026-0500",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Adjudicada,
                FechaCreacion = DateTime.UtcNow.AddDays(-3),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(-1),
            });
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizacionId,
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                ProveedorNombre = "Corralón Sur",
                Flete = 1000m,
                PlazoEntregaDias = 3,
                FechaCreacion = DateTime.UtcNow.AddDays(-2),
            });
            db.OrdenesCompra.Add(new OrdenCompra
            {
                OrdenCompraId = Guid.NewGuid(),
                LicitacionId = licitacionId,
                CotizacionId = cotizacionId,
                ProveedorId = proveedorId,
                Estado = "Emitida",
                FechaEmision = DateTime.UtcNow.AddDays(-1),
            });
        });

        foreach (var rol in new[] { "Capataz", "Comprador", "Admin" })
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));

            var dto = await Consultar(client, requisicionId);

            Assert.NotNull(dto);
            Assert.Equal(licitacionId, dto.LicitacionId);
            Assert.Equal("Adjudicada", dto.EstadoLicitacion);
            Assert.Equal(1, dto.CantidadCotizaciones);
            var orden = Assert.Single(dto.OrdenesCompra);
            Assert.Equal("Emitida", orden.Estado);
            Assert.Equal("Corralón Sur", orden.ProveedorNombre);
        }
    }

    [Fact]
    public async Task Seguimiento_ElProveedorNoPuedeConsultarlo_403()
    {
        using var factory = CreateIsolatedFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));

        var resp = await client.GetAsync($"/api/licitaciones/seguimiento/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Seguimiento_SinToken_401()
    {
        using var factory = CreateIsolatedFactory();
        var resp = await factory.CreateClient().GetAsync($"/api/licitaciones/seguimiento/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
