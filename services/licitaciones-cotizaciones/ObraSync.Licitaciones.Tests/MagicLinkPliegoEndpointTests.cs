using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ObraSync.Licitaciones.Application.MagicLinks;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

/// <summary>#158: GET público del pliego al que da acceso un Magic Link (no consume el enlace).</summary>
public class MagicLinkPliegoEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public MagicLinkPliegoEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext> seed)
    {
        var dbName = $"DB_Licit_MagicLinkPliego_{Guid.NewGuid()}";

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
                services.AddDbContext<LicitacionesDbContext>(options => options.UseInMemoryDatabase(dbName));

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
                seed(db);
                db.SaveChanges();
            });
        });
    }

    private static Licitacion CrearLicitacion(
        Guid licitacionId,
        Guid articuloId,
        EstadoLicitacion estado = EstadoLicitacion.Abierta,
        DateTime? fechaLimite = null) => new()
    {
        LicitacionId = licitacionId,
        RequisicionId = Guid.NewGuid(),
        NumeroRequisicion = "REQ-2026-0158",
        ObraId = Guid.NewGuid(),
        Estado = estado,
        FechaCreacion = DateTime.UtcNow.AddHours(-1),
        FechaLimiteCotizacion = fechaLimite ?? DateTime.UtcNow.AddDays(3),
        Items = new List<LicitacionItem>
        {
            new()
            {
                Id = Guid.NewGuid(),
                LicitacionId = licitacionId,
                ArticuloId = articuloId,
                Descripcion = "Cemento Portland",
                Cantidad = 80m,
                UnidadMedida = "BOLSA"
            }
        }
    };

    private static MagicLink CrearEnlace(
        string token,
        Guid licitacionId,
        DateTime? vencimiento = null,
        DateTime? fechaDeUso = null) => new()
    {
        Id = Guid.NewGuid(),
        Hash = MagicLinkHashHelper.ComputeSha256(token),
        LicitacionId = licitacionId,
        ProveedorId = Guid.NewGuid(),
        ProveedorNombre = "Corralón Norte",
        FechaDeVencimiento = vencimiento ?? DateTime.UtcNow.AddHours(1),
        FechaDeUso = fechaDeUso
    };

    [Fact]
    public async Task GetPliego_EnlaceValido_DevuelveElPliegoSinTokenYSinConsumirElEnlace()
    {
        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        const string token = "token-pliego-valido";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacion(licitacionId, articuloId));
            db.MagicLinks.Add(CrearEnlace(token, licitacionId));
        });

        var client = factory.CreateClient(); // sin Authorization: es público
        var resp = await client.GetAsync($"/api/licitaciones/magic-links/{token}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var raiz = json.RootElement;
        Assert.Equal(licitacionId, raiz.GetProperty("licitacionId").GetGuid());
        Assert.Equal("REQ-2026-0158", raiz.GetProperty("numeroRequisicion").GetString());
        Assert.Equal("Corralón Norte", raiz.GetProperty("proveedorNombre").GetString());
        Assert.True(raiz.TryGetProperty("fechaLimiteCotizacion", out _));
        Assert.True(raiz.TryGetProperty("fechaDeVencimientoEnlace", out _));

        var item = Assert.Single(raiz.GetProperty("items").EnumerateArray());
        Assert.Equal(articuloId, item.GetProperty("articuloId").GetGuid());
        Assert.Equal("Cemento Portland", item.GetProperty("descripcion").GetString());
        Assert.Equal(80m, item.GetProperty("cantidad").GetDecimal());
        Assert.Equal("BOLSA", item.GetProperty("unidadMedida").GetString());

        // Vista pública: no se filtran la obra, la requisición ni el proveedor.
        Assert.False(raiz.TryGetProperty("obraId", out _));
        Assert.False(raiz.TryGetProperty("requisicionId", out _));
        Assert.False(raiz.TryGetProperty("proveedorId", out _));

        // Mirarlo no gasta el enlace: se puede volver a abrir y todavía se puede cotizar con él.
        var otraVez = await client.GetAsync($"/api/licitaciones/magic-links/{token}");
        Assert.Equal(HttpStatusCode.OK, otraVez.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Null((await db.MagicLinks.SingleAsync()).FechaDeUso);

        var cotizar = await client.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{token}/cotizaciones",
            new { flete = 100m, plazoEntregaDias = 3, items = new[] { new { articuloId, precioUnitario = 50m } } });
        Assert.Equal(HttpStatusCode.OK, cotizar.StatusCode);

        // Ya cotizado, el enlace está gastado y el GET lo dice (409).
        var gastado = await client.GetAsync($"/api/licitaciones/magic-links/{token}");
        Assert.Equal(HttpStatusCode.Conflict, gastado.StatusCode);
    }

    public static IEnumerable<object[]> CasosDeError() => new[]
    {
        new object[] { "inexistente", HttpStatusCode.NotFound },
        new object[] { "usado", HttpStatusCode.Conflict },
        new object[] { "vencido", HttpStatusCode.BadRequest },
        new object[] { "licitacion-cerrada", HttpStatusCode.BadRequest },
        new object[] { "plazo-vencido", HttpStatusCode.BadRequest },
    };

    [Theory]
    [MemberData(nameof(CasosDeError))]
    public async Task GetPliego_EnlaceNoUtilizable_DevuelveProblemDetailsConCorrelationId(
        string caso,
        HttpStatusCode esperado)
    {
        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var token = $"token-{caso}";

        using var factory = CreateIsolatedFactory(db =>
        {
            switch (caso)
            {
                case "usado":
                    db.Licitaciones.Add(CrearLicitacion(licitacionId, articuloId));
                    db.MagicLinks.Add(CrearEnlace(token, licitacionId, fechaDeUso: DateTime.UtcNow.AddMinutes(-1)));
                    break;
                case "vencido":
                    db.Licitaciones.Add(CrearLicitacion(licitacionId, articuloId));
                    db.MagicLinks.Add(CrearEnlace(token, licitacionId, vencimiento: DateTime.UtcNow.AddMinutes(-5)));
                    break;
                case "licitacion-cerrada":
                    db.Licitaciones.Add(CrearLicitacion(licitacionId, articuloId, EstadoLicitacion.Cerrada));
                    db.MagicLinks.Add(CrearEnlace(token, licitacionId));
                    break;
                case "plazo-vencido":
                    db.Licitaciones.Add(CrearLicitacion(licitacionId, articuloId, fechaLimite: DateTime.UtcNow.AddMinutes(-10)));
                    db.MagicLinks.Add(CrearEnlace(token, licitacionId));
                    break;
            }
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", $"corr-pliego-{caso}");

        var resp = await client.GetAsync($"/api/licitaciones/magic-links/{token}");

        Assert.Equal(esperado, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal((int)esperado, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()));
        Assert.Equal($"corr-pliego-{caso}", json.RootElement.GetProperty("correlationId").GetString());
    }
}
