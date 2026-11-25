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

/// <summary>
/// Tarjeta #255: El endpoint de Magic Link debe aceptar únicamente el token en texto plano
/// y rechazar el hash SHA-256 almacenado en la base de datos tanto en GET como en POST,
/// manteniendo intacto el consumo atómico de un solo uso.
/// </summary>
public class MagicLinkSoloTokenNoHashTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public MagicLinkSoloTokenNoHashTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_MagicSoloToken_{Guid.NewGuid()}";

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

    private static (Guid LicitacionId, Guid ItemPliegoId, Guid ArticuloId) SembrarLicitacion(LicitacionesDbContext db)
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = DateTime.UtcNow.AddHours(-1),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(4),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = itemPliegoId,
                    LicitacionId = licitacionId,
                    ArticuloId = articuloId,
                    Descripcion = "Cemento Portland",
                    Cantidad = 10m,
                    UnidadMedida = "BOLSA"
                }
            }
        });

        return (licitacionId, itemPliegoId, articuloId);
    }

    private static async Task AssertNotFoundProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedCorrelationId,
        HttpStatusCode expectedStatus = HttpStatusCode.NotFound)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal((int)expectedStatus, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCorrelationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task HashGuardadoEnBd_NoSirveEnGetNiEnPost_TokenPlanoFuncionaYSigueSiendoDeUnSoloUso()
    {
        Guid licId = Guid.Empty;
        Guid itemPliegoId = Guid.Empty;
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, itemPliegoId, _) = SembrarLicitacion(db);
        });

        // 1. Generar un Magic Link real como Comprador
        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var genResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licId}/magic-links",
            new { proveedorId, proveedorNombre = "Corralón del Valle" });
        Assert.Equal(HttpStatusCode.OK, genResp.StatusCode);

        using var genDoc = JsonDocument.Parse(await genResp.Content.ReadAsStringAsync());
        var tokenPlano = genDoc.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(tokenPlano));

        string hashGuardado;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var enlace = await db.MagicLinks.SingleAsync();
            hashGuardado = enlace.Hash;
            Assert.NotEqual(tokenPlano, hashGuardado);
            Assert.Equal(MagicLinkHashHelper.ComputeSha256(tokenPlano!), hashGuardado);
            Assert.Null(enlace.FechaDeUso);
        }

        // 2. Intentar consultar GET usando el hash guardado en BD -> 404 ProblemDetails con correlationId
        var clientAnon = factory.CreateClient();
        var cidGetHash = Guid.NewGuid().ToString();
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cidGetHash);

        var respGetHash = await clientAnon.GetAsync($"/api/licitaciones/magic-links/{hashGuardado}");
        await AssertNotFoundProblemDetailsAsync(respGetHash, cidGetHash, HttpStatusCode.NotFound);

        // 3. Intentar cotizar POST usando el hash guardado en BD -> 404 ProblemDetails, sin crear cotización ni consumir enlace
        var cidPostHash = Guid.NewGuid().ToString();
        clientAnon.DefaultRequestHeaders.Remove("X-Correlation-ID");
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cidPostHash);

        var payloadCotizacion = new
        {
            flete = 200m,
            plazoEntregaDias = 3,
            items = new[]
            {
                new { licitacionItemId = itemPliegoId, precioUnitario = 1500m }
            }
        };

        var respPostHash = await clientAnon.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{hashGuardado}/cotizaciones",
            payloadCotizacion);
        await AssertNotFoundProblemDetailsAsync(respPostHash, cidPostHash, HttpStatusCode.NotFound);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            Assert.Equal(0, await db.Cotizaciones.CountAsync());
            var enlace = await db.MagicLinks.SingleAsync();
            Assert.Null(enlace.FechaDeUso);
        }

        // 4. Usar el token real en claro -> GET 200 OK y POST 200 OK (consumido una única vez)
        var respGetToken = await clientAnon.GetAsync($"/api/licitaciones/magic-links/{tokenPlano}");
        Assert.Equal(HttpStatusCode.OK, respGetToken.StatusCode);

        var respPostToken = await clientAnon.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{tokenPlano}/cotizaciones",
            payloadCotizacion);
        Assert.Equal(HttpStatusCode.OK, respPostToken.StatusCode);

        var cotizacionDto = await respPostToken.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(cotizacionDto);
        Assert.Equal(licId, cotizacionDto.LicitacionId);
        Assert.Equal(proveedorId, cotizacionDto.ProveedorId);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            Assert.Equal(1, await db.Cotizaciones.CountAsync());
            var enlace = await db.MagicLinks.SingleAsync();
            Assert.NotNull(enlace.FechaDeUso);
        }

        // 5. Segundo intento con el mismo token plano -> 409 Conflict ProblemDetails, sigue existiendo 1 sola cotización
        var cidSegundoUso = Guid.NewGuid().ToString();
        clientAnon.DefaultRequestHeaders.Remove("X-Correlation-ID");
        clientAnon.DefaultRequestHeaders.Add("X-Correlation-ID", cidSegundoUso);

        var respSegundoUso = await clientAnon.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{tokenPlano}/cotizaciones",
            payloadCotizacion);
        await AssertNotFoundProblemDetailsAsync(respSegundoUso, cidSegundoUso, HttpStatusCode.Conflict);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            Assert.Equal(1, await db.Cotizaciones.CountAsync());
        }
    }

    [Fact]
    public async Task ConsumoAtomicoConTokenRealEnParalelo_SoloPermiteUnaCotizacion()
    {
        Guid licId = Guid.Empty;
        Guid itemPliegoId = Guid.Empty;
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, itemPliegoId, _) = SembrarLicitacion(db);
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var genResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licId}/magic-links",
            new { proveedorId });
        Assert.Equal(HttpStatusCode.OK, genResp.StatusCode);

        using var genDoc = JsonDocument.Parse(await genResp.Content.ReadAsStringAsync());
        var tokenPlano = genDoc.RootElement.GetProperty("token").GetString()!;

        var payload = new
        {
            flete = 150m,
            plazoEntregaDias = 2,
            items = new[]
            {
                new { licitacionItemId = itemPliegoId, precioUnitario = 950m }
            }
        };

        var c1 = factory.CreateClient();
        var c2 = factory.CreateClient();
        var responses = await Task.WhenAll(
            c1.PostAsJsonAsync($"/api/licitaciones/magic-links/{tokenPlano}/cotizaciones", payload),
            c2.PostAsJsonAsync($"/api/licitaciones/magic-links/{tokenPlano}/cotizaciones", payload));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Equal(1, await db.Cotizaciones.CountAsync());
    }
}
