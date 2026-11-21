using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class ScoringConfiguracionYNormalizacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public ScoringConfiguracionYNormalizacionTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        Action<LicitacionesDbContext>? seedAction = null,
        Dictionary<string, string?>? scoringSettings = null)
    {
        var dbName = $"DB_Licit_Scoring197_{Guid.NewGuid()}";

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

            if (scoringSettings != null)
            {
                foreach (var kvp in scoringSettings)
                {
                    builder.UseSetting(kvp.Key, kvp.Value);
                }
            }

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

    [Fact]
    public async Task CambiarPesosPorConfiguracion_InvierteLaOfertaGanadoraYReflejaPesosEnDesgloseScoring()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var provBaratoLento = Guid.NewGuid();
        var provCaroRapido = Guid.NewGuid();

        Action<LicitacionesDbContext> seed = db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-1971",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = itemPliegoId,
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 1m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            // Oferta A: más barata ($100.000) pero lenta (10 días)
            var cotA = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotA,
                LicitacionId = licitacionId,
                ProveedorId = provBaratoLento,
                Flete = 0m,
                PlazoEntregaDias = 10,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-10),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cotA, ArticuloId = articuloId, PrecioUnitario = 100000m }
                }
            });

            // Oferta B: más cara ($130.000) pero entrega rápida (2 días)
            var cotB = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotB,
                LicitacionId = licitacionId,
                ProveedorId = provCaroRapido,
                Flete = 0m,
                PlazoEntregaDias = 2,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cotB, ArticuloId = articuloId, PrecioUnitario = 130000m }
                }
            });
        };

        // 1. Con configuración priorizando Precio (PesoPrecio = 0.80, PesoPlazo = 0.10, PesoFlete = 0.10) -> gana provBaratoLento
        using (var factoryPrecio = CreateIsolatedFactory(seed, new Dictionary<string, string?>
        {
            ["Scoring:PesoPrecio"] = "0.80",
            ["Scoring:PesoPlazo"] = "0.10",
            ["Scoring:PesoFlete"] = "0.10",
            ["Scoring:PesoCalificacion"] = "0.00"
        }))
        {
            var client = factoryPrecio.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

            var lista = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
                $"/api/cotizaciones?licitacionId={licitacionId}");
            Assert.NotNull(lista);

            var ofertaA = Assert.Single(lista, c => c.ProveedorId == provBaratoLento);
            var ofertaB = Assert.Single(lista, c => c.ProveedorId == provCaroRapido);

            Assert.True(ofertaA.EsGanadora);
            Assert.False(ofertaB.EsGanadora);
            Assert.NotNull(ofertaA.DesgloseScoring);
            Assert.Equal(0.80m, ofertaA.DesgloseScoring!.PesoPrecio);
            Assert.Equal(0.10m, ofertaA.DesgloseScoring.PesoPlazo);
        }

        // 2. Con configuración priorizando Plazo (PesoPrecio = 0.10, PesoPlazo = 0.80, PesoFlete = 0.10) -> gana provCaroRapido
        using (var factoryPlazo = CreateIsolatedFactory(seed, new Dictionary<string, string?>
        {
            ["Scoring:PesoPrecio"] = "0.10",
            ["Scoring:PesoPlazo"] = "0.80",
            ["Scoring:PesoFlete"] = "0.10",
            ["Scoring:PesoCalificacion"] = "0.00"
        }))
        {
            var client = factoryPlazo.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

            var lista = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
                $"/api/cotizaciones?licitacionId={licitacionId}");
            Assert.NotNull(lista);

            var ofertaA = Assert.Single(lista, c => c.ProveedorId == provBaratoLento);
            var ofertaB = Assert.Single(lista, c => c.ProveedorId == provCaroRapido);

            Assert.False(ofertaA.EsGanadora);
            Assert.True(ofertaB.EsGanadora);
            Assert.NotNull(ofertaB.DesgloseScoring);
            Assert.Equal(0.10m, ofertaB.DesgloseScoring!.PesoPrecio);
            Assert.Equal(0.80m, ofertaB.DesgloseScoring.PesoPlazo);
        }
    }

    [Theory]
    [InlineData("0", "0", "0", "0")]
    [InlineData("-0.1", "0.5", "0.4", "0")]
    [InlineData("0.5", "-0.2", "0.3", "0")]
    [InlineData("0.5", "0.2", "-0.1", "0")]
    [InlineData("0.5", "0.2", "0.3", "-0.1")]
    public void Arrancar_ConPesosDeScoringInvalidos_FallaConInvalidOperationException(
        string pesoPrecio,
        string pesoPlazo,
        string pesoFlete,
        string pesoCalificacion)
    {
        using var factory = CreateIsolatedFactory(scoringSettings: new Dictionary<string, string?>
        {
            ["Scoring:PesoPrecio"] = pesoPrecio,
            ["Scoring:PesoPlazo"] = pesoPlazo,
            ["Scoring:PesoFlete"] = pesoFlete,
            ["Scoring:PesoCalificacion"] = pesoCalificacion
        });

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var invalidOp = ex as InvalidOperationException ?? ex.InnerException as InvalidOperationException;
        Assert.NotNull(invalidOp);
        Assert.Contains("Scoring", invalidOp.Message);
    }

    [Fact]
    public async Task DosOfertasCasiIguales_1PorCientoDeDiferenciaEnPrecio_NoCastigaConCeroALaSegundaYPermiteQueMejorPlazoGane()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var prov100 = Guid.NewGuid();
        var prov101 = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-1972",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = itemPliegoId,
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 1m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            // Oferta 1: $100.000, plazo 10 días, flete 0 -> Mejor en todo cuando empata plazo, pero acá plazo es 10 días
            var cot1 = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cot1,
                LicitacionId = licitacionId,
                ProveedorId = prov100,
                Flete = 0m,
                PlazoEntregaDias = 10,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-10),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cot1, ArticuloId = articuloId, PrecioUnitario = 100000m }
                }
            });

            // Oferta 2: $101.000 (apenas 1% más cara), pero entrega en 5 días (el doble de rápido)
            var cot2 = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cot2,
                LicitacionId = licitacionId,
                ProveedorId = prov101,
                Flete = 0m,
                PlazoEntregaDias = 5,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cot2, ArticuloId = articuloId, PrecioUnitario = 101000m }
                }
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var lista = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacionId}");
        Assert.NotNull(lista);

        var oferta100 = Assert.Single(lista, c => c.ProveedorId == prov100);
        var oferta101 = Assert.Single(lista, c => c.ProveedorId == prov101);

        Assert.NotNull(oferta100.DesgloseScoring);
        Assert.NotNull(oferta101.DesgloseScoring);

        // Con normalización relativa (mejor / valor):
        // Oferta 1 ($100.000): PuntajePrecio = 100.00, PuntajePlazo = 5/10 = 50.00, PuntajeFlete = 100.00
        // Oferta 2 ($101.000): PuntajePrecio = 100000/101000 = 99.01 (NO 0!), PuntajePlazo = 100.00, PuntajeFlete = 100.00
        Assert.Equal(100m, oferta100.DesgloseScoring!.PuntajePrecio);
        Assert.Equal(50m, oferta100.DesgloseScoring.PuntajePlazo);

        Assert.Equal(99.01m, oferta101.DesgloseScoring!.PuntajePrecio);
        Assert.Equal(100m, oferta101.DesgloseScoring.PuntajePlazo);
        Assert.Equal(0.10m, oferta101.DesgloseScoring.PesoCalificacion);

        // Gracias a que el 1% de precio solo le resta ~0.5 puntos globales y gana en plazo,
        // la Oferta 2 gana legítimamente la comparativa
        Assert.True(oferta101.PuntajeTotal > oferta100.PuntajeTotal);
        Assert.True(oferta101.EsGanadora);
        Assert.False(oferta100.EsGanadora);
    }

    [Fact]
    public async Task EsGanadora_SoloCuentaOfertasQueEntranAlScoring_UnaCompletaYUnaParcialNoMarcaGanadora()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-1973",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-2),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = itemPliegoId,
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland (bolsa 25 kg)",
                        Cantidad = 2m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            // 1 oferta completa
            var cotCompleta = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotCompleta,
                LicitacionId = licitacionId,
                ProveedorId = Guid.NewGuid(),
                Flete = 0m,
                PlazoEntregaDias = 3,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-10),
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotCompleta,
                        ArticuloId = articuloId,
                        PrecioUnitario = 10000m,
                        CoberturaParcial = false
                    }
                }
            });

            // 1 oferta con cobertura parcial (no entra al scoring de completas)
            var cotParcial = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotParcial,
                LicitacionId = licitacionId,
                ProveedorId = Guid.NewGuid(),
                Flete = 0m,
                PlazoEntregaDias = 1,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-5),
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotParcial,
                        ArticuloId = articuloId,
                        PrecioUnitario = 5000m,
                        PresentacionOfrecida = "bolsa 25 kg",
                        CantidadOfrecida = 1m,
                        CoberturaParcial = true
                    }
                }
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var lista = await client.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacionId}");
        Assert.NotNull(lista);
        Assert.Equal(2, lista.Count);

        // Aunque hay 2 filas en la tabla, solo 1 entró al scoring (la otra es parcial), por lo que ninguna es EsGanadora
        Assert.All(lista, c => Assert.False(c.EsGanadora));
    }
}
