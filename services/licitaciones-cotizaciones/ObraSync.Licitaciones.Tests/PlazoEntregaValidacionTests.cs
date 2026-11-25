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
/// Tarjeta #251: Validar que plazoEntregaDias sea un entero positivo en días corridos (>= 1)
/// (y <= 15: máximo 15 días corridos desde que se cotiza) en POST /api/cotizaciones, PUT /api/cotizaciones/{id} y POST /api/licitaciones/magic-links/{hash}/cotizaciones.
/// </summary>
public class PlazoEntregaValidacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PlazoEntregaValidacionTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Plazo251_{Guid.NewGuid()}";

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

    private static (Guid LicitacionId, Guid ArticuloId) SembrarLicitacionAbierta(LicitacionesDbContext db)
    {
        var licitacionId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        db.Licitaciones.Add(new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    LicitacionId = licitacionId,
                    ArticuloId = articuloId,
                    Descripcion = "Arena gruesa",
                    Cantidad = 10m,
                    UnidadMedida = "M3"
                }
            }
        });

        return (licitacionId, articuloId);
    }

    private static async Task AssertBadRequestPlazoProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedCorrelationId)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCorrelationId, doc.RootElement.GetProperty("correlationId").GetString());

        var detail = doc.RootElement.GetProperty("detail").GetString();
        Assert.NotNull(detail);
        Assert.Contains("días", detail, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(16)]
    [InlineData(365)]
    public async Task PostCotizacion_PlazoFueraDeRango_Retorna400ProblemDetailsConUnidadYCorrelationId(int plazoInvalido)
    {
        Guid licId = Guid.Empty;
        Guid artId = Guid.Empty;
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, artId) = SembrarLicitacionAbierta(db);
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = plazoInvalido,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = artId, PrecioUnitario = 1500m }
            }
        });

        await AssertBadRequestPlazoProblemDetailsAsync(resp, cid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(16)]
    [InlineData(365)]
    public async Task PutCotizacion_PlazoFueraDeRango_Retorna400ProblemDetailsConUnidadYCorrelationId(int plazoInvalido)
    {
        Guid licId = Guid.Empty;
        Guid artId = Guid.Empty;
        var cotizacionId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, artId) = SembrarLicitacionAbierta(db);
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotizacionId,
                LicitacionId = licId,
                ProveedorId = proveedorId,
                Flete = 200m,
                PlazoEntregaDias = 4,
                FechaCreacion = DateTime.UtcNow.AddMinutes(-30),
                Items = new List<CotizacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        CotizacionId = cotizacionId,
                        ArticuloId = artId,
                        PrecioUnitario = 2000m
                    }
                }
            });
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PutAsJsonAsync($"/api/cotizaciones/{cotizacionId}", new CrearCotizacionRequestDto
        {
            LicitacionId = licId,
            ProveedorId = proveedorId,
            Flete = 250m,
            PlazoEntregaDias = plazoInvalido,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = artId, PrecioUnitario = 2100m }
            }
        });

        await AssertBadRequestPlazoProblemDetailsAsync(resp, cid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(16)]
    [InlineData(365)]
    public async Task MagicLinkCotizar_PlazoFueraDeRango_Retorna400ProblemDetailsSinQuemarElEnlace(int plazoInvalido)
    {
        Guid licId = Guid.Empty;
        Guid artId = Guid.Empty;
        var proveedorId = Guid.NewGuid();
        var rawToken = $"token-plazo-{Guid.NewGuid():N}";

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, artId) = SembrarLicitacionAbierta(db);
            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(rawToken),
                LicitacionId = licId,
                ProveedorId = proveedorId,
                FechaDeVencimiento = DateTime.UtcNow.AddHours(6),
                FechaDeUso = null
            });
        });

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var respInvalido = await client.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{rawToken}/cotizaciones",
            new
            {
                flete = 100m,
                plazoEntregaDias = plazoInvalido,
                items = new[]
                {
                    new { articuloId = artId, precioUnitario = 1500m }
                }
            });

        await AssertBadRequestPlazoProblemDetailsAsync(respInvalido, cid);

        // El enlace no se quemó tras el 400: reintentar con el mínimo válido (1 día) funciona (200 OK)
        var respValido = await client.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{rawToken}/cotizaciones",
            new
            {
                flete = 100m,
                plazoEntregaDias = 1,
                items = new[]
                {
                    new { articuloId = artId, precioUnitario = 1500m }
                }
            });

        Assert.Equal(HttpStatusCode.OK, respValido.StatusCode);
        var dto = await respValido.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(1, dto.PlazoEntregaDias);
    }

    [Fact]
    public async Task PostYPutCotizacion_ConMinimoValido1Dia_SonAceptadosCorrectamente()
    {
        Guid licId = Guid.Empty;
        Guid artId = Guid.Empty;
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, artId) = SembrarLicitacionAbierta(db);
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var postResp = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licId,
            ProveedorId = proveedorId,
            Flete = 50m,
            PlazoEntregaDias = 1,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = artId, PrecioUnitario = 1200m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);
        var creada = await postResp.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(creada);
        Assert.Equal(1, creada.PlazoEntregaDias);

        var putResp = await client.PutAsJsonAsync($"/api/cotizaciones/{creada.CotizacionId}", new CrearCotizacionRequestDto
        {
            LicitacionId = licId,
            ProveedorId = proveedorId,
            Flete = 75m,
            PlazoEntregaDias = 1,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = artId, PrecioUnitario = 1150m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, putResp.StatusCode);
        var corregida = await putResp.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(corregida);
        Assert.Equal(1, corregida.PlazoEntregaDias);
    }

    [Theory]
    [InlineData("BOLSA", null)]
    [InlineData("M3", "Bolsa de 25 kg")]
    public async Task PostCotizacion_FraccionDeBolsa_Retorna400(string unidadPliego, string? presentacion)
    {
        Guid licId = Guid.Empty;
        Guid artId = Guid.Empty;
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            (licId, artId) = SembrarLicitacionAbierta(db);
            db.Licitaciones.Local.Single(l => l.LicitacionId == licId).Items.Single().UnidadMedida = unidadPliego;
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var resp = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = artId, PrecioUnitario = 1500m, PresentacionOfrecida = presentacion, CantidadOfrecida = 1.4m }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("bolsas", await resp.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
