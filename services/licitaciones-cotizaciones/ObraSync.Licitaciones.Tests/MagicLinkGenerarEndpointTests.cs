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
using ObraSync.Licitaciones.Application.MagicLinks;
using ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class MagicLinkGenerarEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public MagicLinkGenerarEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        Action<LicitacionesDbContext>? seedAction = null,
        string? horasVigenciaConfig = null)
    {
        var dbName = $"DB_Licit_GenMagicLink_{Guid.NewGuid()}";

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("RabbitMQ:UseInMemory", "true");
            if (horasVigenciaConfig != null)
            {
                builder.UseSetting("MagicLink:HorasVigencia", horasVigenciaConfig);
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

    private static async Task AssertProblemDetailsAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCorrelationId)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GenerarMagicLink_LicitacionInexistente_Devuelve404ProblemDetailsConCorrelationId()
    {
        using var factory = CreateIsolatedFactory();
        var correlationId = Guid.NewGuid().ToString();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{Guid.NewGuid()}/magic-links",
            new { proveedorId = Guid.NewGuid() });

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, correlationId);
    }

    [Theory]
    [InlineData(EstadoLicitacion.Cerrada)]
    [InlineData(EstadoLicitacion.Adjudicada)]
    public async Task GenerarMagicLink_LicitacionCerradaOAdjudicada_Devuelve409ConflictConProblemDetailsYCorrelationId(EstadoLicitacion estadoNoAbierto)
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacion(licitacionId, itemPliegoId, articuloId, estadoNoAbierto, DateTime.UtcNow.AddDays(3)));
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/magic-links",
            new { proveedorId = Guid.NewGuid() });

        await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict, correlationId);
    }

    [Fact]
    public async Task GenerarMagicLink_LicitacionConFechaLimiteVencida_Devuelve409ConflictConProblemDetailsYCorrelationId()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacion(
                licitacionId,
                itemPliegoId,
                articuloId,
                EstadoLicitacion.Abierta,
                DateTime.UtcNow.AddMinutes(-10)));
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/magic-links",
            new { proveedorId = Guid.NewGuid() });

        await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict, correlationId);
    }

    [Fact]
    public async Task GenerarMagicLink_TokenEnClaroDe32BytesUrlSafeNoQuedaEnBase_SoloSeGuardaSuSha256YFechaDeVencimientoCoincide()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacion(licitacionId, itemPliegoId, articuloId, EstadoLicitacion.Abierta, DateTime.UtcNow.AddDays(5)));
        }, horasVigenciaConfig: "6");

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/magic-links",
            new { proveedorId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var dto = JsonSerializer.Deserialize<GenerarMagicLinkResponseDto>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(dto);

        // 1. El token en claro proviene de 32 bytes en Base64 URL-safe (43 caracteres, sin '+', '/' ni '=')
        Assert.Equal(43, dto.Token.Length);
        Assert.DoesNotContain("+", dto.Token);
        Assert.DoesNotContain("/", dto.Token);
        Assert.DoesNotContain("=", dto.Token);
        Assert.Equal(dto.Token, dto.Hash);
        Assert.Equal($"/api/licitaciones/magic-links/{dto.Token}/cotizaciones", dto.Url);
        Assert.Equal("6 hours", dto.ExpiresIn);
        Assert.Equal(6.0, dto.HorasVigencia);

        // 2. En la base de datos NUNCA queda el token en claro, sino únicamente su SHA-256 (64 hex chars)
        // y la FechaDeVencimiento devuelta coincide exactamente con la persistida en la base de datos.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var linkEnBase = await db.MagicLinks.SingleAsync();

            var sha256Esperado = MagicLinkHashHelper.ComputeSha256(dto.Token);
            Assert.NotEqual(dto.Token, linkEnBase.TokenHash);
            Assert.NotEqual(dto.Token, linkEnBase.Hash);
            Assert.Equal(64, linkEnBase.TokenHash.Length);
            Assert.Equal(sha256Esperado, linkEnBase.TokenHash);
            Assert.Equal(sha256Esperado, linkEnBase.Hash);
            Assert.Equal(linkEnBase.FechaDeVencimiento, dto.FechaDeVencimiento);
        }

        // 3. El mismo token devuelto se acepta una única vez al cotizar
        var clientAnonimo = factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString();
        clientAnonimo.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var cotizarPayload = new
        {
            flete = 120m,
            plazoEntregaDias = 3,
            items = new[] { new { licitacionItemId = itemPliegoId, precioUnitario = 85m } }
        };

        var primeraCotizacionResp = await clientAnonimo.PostAsJsonAsync(dto.Url, cotizarPayload);
        Assert.Equal(HttpStatusCode.OK, primeraCotizacionResp.StatusCode);

        var segundaCotizacionResp = await clientAnonimo.PostAsJsonAsync(dto.Url, cotizarPayload);
        await AssertProblemDetailsAsync(segundaCotizacionResp, HttpStatusCode.Conflict, correlationId);
    }

    private static Licitacion CrearLicitacion(
        Guid licitacionId,
        Guid itemPliegoId,
        Guid articuloId,
        EstadoLicitacion estado,
        DateTime fechaLimiteCotizacion)
    {
        return new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0900",
            ObraId = Guid.NewGuid(),
            Estado = estado,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = fechaLimiteCotizacion,
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
        };
    }
}
