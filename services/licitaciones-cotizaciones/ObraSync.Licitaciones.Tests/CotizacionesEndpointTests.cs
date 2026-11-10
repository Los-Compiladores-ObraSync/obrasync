using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ObraSync.Licitaciones.Tests;

public class CotizacionesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    public CotizacionesEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _dbName = $"DB_Licit_Cotizaciones_{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
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
                    options.UseInMemoryDatabase(_dbName);
                });
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
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<Licitacion> SembrarLicitacionAsync(
        EstadoLicitacion estado = EstadoLicitacion.Abierta,
        DateTime? fechaLimite = null,
        params Guid[] articulosIds)
    {
        var itemsIds = articulosIds.Length > 0
            ? articulosIds
            : new[] { Guid.NewGuid(), Guid.NewGuid() };

        var licitacion = new Licitacion
        {
            LicitacionId = Guid.NewGuid(),
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0100",
            ObraId = Guid.NewGuid(),
            Estado = estado,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = fechaLimite ?? DateTime.UtcNow.AddDays(5),
            Items = itemsIds.Select(artId => new LicitacionItem
            {
                Id = Guid.NewGuid(),
                ArticuloId = artId,
                Descripcion = $"Artículo {artId:N}",
                Cantidad = 50m,
                UnidadMedida = "BOLSA"
            }).ToList()
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        db.Licitaciones.Add(licitacion);
        await db.SaveChangesAsync();

        return licitacion;
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

    [Fact]
    public async Task PostCotizacion_ProveedorConSuPropioProveedorId_Retorna201CreatedYPersisteConFechaUtc()
    {
        var articulo1 = Guid.NewGuid();
        var articulo2 = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo1, articulo2);
        var proveedorId = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var request = new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 15000.50m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 8400m },
                new() { ArticuloId = articulo2, PrecioUnitario = 4200.75m }
            }
        };

        var response = await client.PostAsJsonAsync("/api/cotizaciones", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.EndsWith("Z", doc.RootElement.GetProperty("fechaCreacion").GetString());

        var creada = doc.RootElement.Deserialize<CotizacionResponseDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(creada);
        Assert.NotEqual(Guid.Empty, creada.CotizacionId);
        Assert.Equal(licitacion.LicitacionId, creada.LicitacionId);
        Assert.Equal(proveedorId, creada.ProveedorId);
        Assert.Equal(15000.50m, creada.Flete);
        Assert.Equal(5, creada.PlazoEntregaDias);
        Assert.Equal(2, creada.Items.Count);
    }

    [Fact]
    public async Task PostCotizacion_AdminCotizandoEnNombreDeUnProveedor_Retorna201Created()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);
        var adminId = Guid.NewGuid();
        var proveedorTerceroId = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin", adminId));

        var request = new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorTerceroId,
            Flete = 0m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 9100m }
            }
        };

        var response = await client.PostAsJsonAsync("/api/cotizaciones", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var creada = await response.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(creada);
        Assert.Equal(proveedorTerceroId, creada.ProveedorId);
    }

    [Fact]
    public async Task PostCotizacion_ProveedorIntentandoImpersonarAOtroProveedor_Retorna403ForbiddenConProblemDetailsYCorrelationId()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);
        var proveedorAutenticadoId = Guid.NewGuid();
        var otroProveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorAutenticadoId));

        var request = new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = otroProveedorId,
            Flete = 5000m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 8000m }
            }
        };

        var response = await client.PostAsJsonAsync("/api/cotizaciones", request);
        await AssertProblemDetailsWithCorrelationIdAsync(response, HttpStatusCode.Forbidden, correlationId);
    }

    [Fact]
    public async Task PostCotizacion_LicitacionInexistente_Retorna404NotFoundConProblemDetailsYCorrelationId()
    {
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var request = new CrearCotizacionRequestDto
        {
            LicitacionId = Guid.NewGuid(),
            ProveedorId = proveedorId,
            Flete = 1000m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = Guid.NewGuid(), PrecioUnitario = 5000m }
            }
        };

        var response = await client.PostAsJsonAsync("/api/cotizaciones", request);
        await AssertProblemDetailsWithCorrelationIdAsync(response, HttpStatusCode.NotFound, correlationId);
    }

    [Fact]
    public async Task PostCotizacion_LicitacionCerradaOVencida_Retorna400BadRequest()
    {
        var articulo = Guid.NewGuid();
        var licitacionCerrada = await SembrarLicitacionAsync(EstadoLicitacion.Cerrada, DateTime.UtcNow.AddDays(3), articulo);
        var licitacionVencida = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, DateTime.UtcNow.AddMinutes(-10), articulo);
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // 1. Licitación en estado Cerrada -> 400 Bad Request
        var respCerrada = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionCerrada.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 1000m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 7000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respCerrada, HttpStatusCode.BadRequest, correlationId);

        // 2. Licitación con FechaLimiteCotizacion vencida -> 400 Bad Request
        var respVencida = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionVencida.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 1000m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 7000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respVencida, HttpStatusCode.BadRequest, correlationId);
    }

    [Fact]
    public async Task PostCotizacion_ItemsDiscrepantesOValoresInvalidos_Retorna400BadRequest()
    {
        var articulo1 = Guid.NewGuid();
        var articulo2 = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo1, articulo2);
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // a) Falta un artículo del pliego
        var respFaltante = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 5000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respFaltante, HttpStatusCode.BadRequest, correlationId);

        // b) Sobra un artículo ajeno al pliego
        var respSobrante = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 5000m },
                new() { ArticuloId = articulo2, PrecioUnitario = 6000m },
                new() { ArticuloId = Guid.NewGuid(), PrecioUnitario = 1000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respSobrante, HttpStatusCode.BadRequest, correlationId);

        // c) Flete negativo
        var respFleteNegativo = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = -50m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 5000m },
                new() { ArticuloId = articulo2, PrecioUnitario = 6000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respFleteNegativo, HttpStatusCode.BadRequest, correlationId);

        // d) PrecioUnitario <= 0
        var respPrecioCero = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 0m },
                new() { ArticuloId = articulo2, PrecioUnitario = 6000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respPrecioCero, HttpStatusCode.BadRequest, correlationId);

        // e) PlazoEntregaDias <= 0
        var respPlazoInvalido = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 0,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 5000m },
                new() { ArticuloId = articulo2, PrecioUnitario = 6000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(respPlazoInvalido, HttpStatusCode.BadRequest, correlationId);
    }

    [Fact]
    public async Task PostCotizacion_MismoProveedorCotizaDosVecesLaMismaLicitacion_Retorna409Conflict()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var payload = new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 2500m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 8200m }
            }
        };

        var primeraResp = await client.PostAsJsonAsync("/api/cotizaciones", payload);
        Assert.Equal(HttpStatusCode.Created, primeraResp.StatusCode);

        var segundaResp = await client.PostAsJsonAsync("/api/cotizaciones", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(segundaResp, HttpStatusCode.Conflict, correlationId);
    }

    [Fact]
    public async Task PostCotizacion_SinTokenRetorna401_ConRolCapatazOCompradorRetorna403()
    {
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var payload = new CrearCotizacionRequestDto
        {
            LicitacionId = Guid.NewGuid(),
            ProveedorId = Guid.NewGuid(),
            Flete = 1000m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = Guid.NewGuid(), PrecioUnitario = 5000m }
            }
        };

        // 1. Sin token -> 401 Unauthorized
        var unauthResp = await client.PostAsJsonAsync("/api/cotizaciones", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(unauthResp, HttpStatusCode.Unauthorized, correlationId);

        // 2. Rol Capataz -> 403 Forbidden
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        var forbiddenCapatazResp = await client.PostAsJsonAsync("/api/cotizaciones", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(forbiddenCapatazResp, HttpStatusCode.Forbidden, correlationId);

        // 3. Rol Comprador -> 403 Forbidden
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var forbiddenCompradorResp = await client.PostAsJsonAsync("/api/cotizaciones", payload);
        await AssertProblemDetailsWithCorrelationIdAsync(forbiddenCompradorResp, HttpStatusCode.Forbidden, correlationId);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task GetCotizaciones_ConRolCompradorOAdmin_RetornaListaDeCotizacionesDeLaLicitacionConFechasUtc(string rolConsulta)
    {
        var articulo1 = Guid.NewGuid();
        var articulo2 = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo1, articulo2);
        var proveedor1Id = Guid.NewGuid();
        var proveedor2Id = Guid.NewGuid();

        var client = _factory.CreateClient();

        // Dos proveedores cargan sus cotizaciones para la misma licitación
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor1Id));
        var post1 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor1Id,
            Flete = 10000m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 8500m },
                new() { ArticuloId = articulo2, PrecioUnitario = 4000m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);

        await Task.Delay(20);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor2Id));
        var post2 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor2Id,
            Flete = 12000m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 8100m },
                new() { ArticuloId = articulo2, PrecioUnitario = 4150m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post2.StatusCode);

        // El Comprador o Admin consulta la matriz comparativa
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rolConsulta));

        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var rawJson = await getResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            Assert.EndsWith("Z", elem.GetProperty("fechaCreacion").GetString());
        }

        var cotizaciones = doc.RootElement.Deserialize<List<CotizacionResponseDto>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(cotizaciones);
        Assert.Equal(2, cotizaciones.Count);
        Assert.Equal(proveedor2Id, cotizaciones[0].ProveedorId);
        Assert.Equal(proveedor1Id, cotizaciones[1].ProveedorId);
        Assert.All(cotizaciones, c => Assert.Equal(2, c.Items.Count));
    }

    [Fact]
    public async Task GetCotizaciones_LicitacionExistenteSinCotizaciones_Retorna200OkConListaVacia()
    {
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lista = await response.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(lista);
        Assert.Empty(lista);
    }

    [Fact]
    public async Task GetCotizaciones_SinParametroLicitacionIdOConGuidVacio_Retorna400BadRequestConProblemDetailsYCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // 1. Sin licitacionId en query string
        var respSinParam = await client.GetAsync("/api/cotizaciones");
        await AssertProblemDetailsWithCorrelationIdAsync(respSinParam, HttpStatusCode.BadRequest, correlationId);

        // 2. Con licitacionId = Guid.Empty
        var respGuidVacio = await client.GetAsync($"/api/cotizaciones?licitacionId={Guid.Empty}");
        await AssertProblemDetailsWithCorrelationIdAsync(respGuidVacio, HttpStatusCode.BadRequest, correlationId);
    }

    [Fact]
    public async Task GetCotizaciones_LicitacionInexistente_Retorna404NotFoundConProblemDetailsYCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var response = await client.GetAsync($"/api/cotizaciones?licitacionId={Guid.NewGuid()}");
        await AssertProblemDetailsWithCorrelationIdAsync(response, HttpStatusCode.NotFound, correlationId);
    }

    [Fact]
    public async Task GetCotizaciones_SeguridadPorRol_SinTokenRetorna401_ProveedorOCapatazRetornan403ConProblemDetailsYCorrelationId()
    {
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta);
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        // 1. Sin token -> 401 Unauthorized
        var unauthResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        await AssertProblemDetailsWithCorrelationIdAsync(unauthResp, HttpStatusCode.Unauthorized, correlationId);

        // 2. Rol Proveedor -> 403 Forbidden (un proveedor no puede espiar cotizaciones de la matriz comparativa)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));
        var forbiddenProvResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        await AssertProblemDetailsWithCorrelationIdAsync(forbiddenProvResp, HttpStatusCode.Forbidden, correlationId);

        // 3. Rol Capataz -> 403 Forbidden
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        var forbiddenCapatazResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        await AssertProblemDetailsWithCorrelationIdAsync(forbiddenCapatazResp, HttpStatusCode.Forbidden, correlationId);
    }
}
