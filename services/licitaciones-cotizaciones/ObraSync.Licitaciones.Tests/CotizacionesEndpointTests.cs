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
            issuer: "obrasync-usuarios",
            audience: "obrasync-services",
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

    private async Task<Licitacion> SembrarLicitacionConCantidadesAsync(
        params (Guid ArticuloId, decimal Cantidad)[] articulosConCantidad)
    {
        var licitacion = new Licitacion
        {
            LicitacionId = Guid.NewGuid(),
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0200",
            ObraId = Guid.NewGuid(),
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = articulosConCantidad.Select(item => new LicitacionItem
            {
                Id = Guid.NewGuid(),
                ArticuloId = item.ArticuloId,
                Descripcion = $"Artículo {item.ArticuloId:N}",
                Cantidad = item.Cantidad,
                UnidadMedida = "UNIDAD"
            }).ToList()
        };

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        db.Licitaciones.Add(licitacion);
        await db.SaveChangesAsync();

        return licitacion;
    }

    [Fact]
    public async Task GetCotizaciones_CU07_CalculaExactamenteSubtotalItemsYCostoTotalEnMultiplesOfertas()
    {
        var articulo1 = Guid.NewGuid();
        var articulo2 = Guid.NewGuid();
        // Pliego: 10 unidades de articulo1 y 5 unidades de articulo2
        var licitacion = await SembrarLicitacionConCantidadesAsync(
            (articulo1, 10m),
            (articulo2, 5m));

        var proveedor1Id = Guid.NewGuid();
        var proveedor2Id = Guid.NewGuid();
        var client = _factory.CreateClient();

        // Oferta 1: (10 * 100) + (5 * 200) = 1000 + 1000 = 2000 subtotal + 350.50 flete = 2350.50 costoTotal
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor1Id));
        var post1 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor1Id,
            Flete = 350.50m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 100m },
                new() { ArticuloId = articulo2, PrecioUnitario = 200m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);

        // Oferta 2: (10 * 120) + (5 * 180) = 1200 + 900 = 2100 subtotal + 400 flete = 2500 costoTotal
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor2Id));
        var post2 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor2Id,
            Flete = 400m,
            PlazoEntregaDias = 6,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo1, PrecioUnitario = 120m },
                new() { ArticuloId = articulo2, PrecioUnitario = 180m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post2.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var cotizaciones = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(cotizaciones);
        Assert.Equal(2, cotizaciones.Count);

        var dto1 = Assert.Single(cotizaciones, c => c.ProveedorId == proveedor1Id);
        Assert.Equal(2000m, dto1.SubtotalItems);
        Assert.Equal(350.50m, dto1.Flete);
        Assert.Equal(2350.50m, dto1.CostoTotal);
        Assert.True(dto1.EsMasBarata);

        var dto2 = Assert.Single(cotizaciones, c => c.ProveedorId == proveedor2Id);
        Assert.Equal(2100m, dto2.SubtotalItems);
        Assert.Equal(400m, dto2.Flete);
        Assert.Equal(2500m, dto2.CostoTotal);
        Assert.False(dto2.EsMasBarata);
    }

    [Fact]
    public async Task GetCotizaciones_CU07_CuandoElFleteInvierteAlGanador_ResaltaLaDeMenorCostoTotal()
    {
        var articulo = Guid.NewGuid();
        // Pliego: 10 unidades
        var licitacion = await SembrarLicitacionConCantidadesAsync((articulo, 10m));

        var proveedorA = Guid.NewGuid();
        var proveedorB = Guid.NewGuid();
        var client = _factory.CreateClient();

        // Oferta A: menor precio de ítems (10 * 100 = $1000) + $500 flete = $1500 costoTotal
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var postA = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorA,
            Flete = 500m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 100m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, postA.StatusCode);

        // Oferta B: mayor precio de ítems (10 * 110 = $1100) + $100 flete = $1200 costoTotal
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorB));
        var postB = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorB,
            Flete = 100m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articulo, PrecioUnitario = 110m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, postB.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var cotizaciones = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(cotizaciones);

        var ofertaA = Assert.Single(cotizaciones, c => c.ProveedorId == proveedorA);
        var ofertaB = Assert.Single(cotizaciones, c => c.ProveedorId == proveedorB);

        Assert.Equal(1000m, ofertaA.SubtotalItems);
        Assert.Equal(500m, ofertaA.Flete);
        Assert.Equal(1500m, ofertaA.CostoTotal);
        Assert.False(ofertaA.EsMasBarata);

        Assert.Equal(1100m, ofertaB.SubtotalItems);
        Assert.Equal(100m, ofertaB.Flete);
        Assert.Equal(1200m, ofertaB.CostoTotal);
        Assert.True(ofertaB.EsMasBarata);
    }

    [Fact]
    public async Task GetCotizaciones_CU07_EmpateEnMenorCostoTotal_MarcaAmbasEmpatadasComoEsMasBarata()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionConCantidadesAsync((articulo, 10m));

        var proveedor1 = Guid.NewGuid();
        var proveedor2 = Guid.NewGuid();
        var proveedor3 = Guid.NewGuid();
        var client = _factory.CreateClient();

        // Proveedor 1: $1000 ítems + $200 flete = $1200
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor1));
        await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor1,
            Flete = 200m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 100m } }
        });

        // Proveedor 2: $1100 ítems + $100 flete = $1200 (empata en menor costo total)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor2));
        await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor2,
            Flete = 100m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 110m } }
        });

        // Proveedor 3: $1200 ítems + $150 flete = $1350 (más cara)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor3));
        await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedor3,
            Flete = 150m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 120m } }
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin"));
        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var cotizaciones = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(cotizaciones);
        Assert.Equal(3, cotizaciones.Count);

        var dto1 = Assert.Single(cotizaciones, c => c.ProveedorId == proveedor1);
        var dto2 = Assert.Single(cotizaciones, c => c.ProveedorId == proveedor2);
        var dto3 = Assert.Single(cotizaciones, c => c.ProveedorId == proveedor3);

        Assert.Equal(1200m, dto1.CostoTotal);
        Assert.True(dto1.EsMasBarata);

        Assert.Equal(1200m, dto2.CostoTotal);
        Assert.True(dto2.EsMasBarata);

        Assert.Equal(1350m, dto3.CostoTotal);
        Assert.False(dto3.EsMasBarata);
    }

    [Fact]
    public async Task GetCotizaciones_CU07_UnaSolaOferta_SeMarcaComoEsMasBarataTrue()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionConCantidadesAsync((articulo, 20m));
        var proveedorId = Guid.NewGuid();
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        var postResp = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 250m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 50m } }
        });
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var cotizaciones = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(cotizaciones);
        var unica = Assert.Single(cotizaciones);
        Assert.Equal(1000m, unica.SubtotalItems);
        Assert.Equal(250m, unica.Flete);
        Assert.Equal(1250m, unica.CostoTotal);
        Assert.True(unica.EsMasBarata);
    }

    // ---- Tarjeta #162: GET /api/cotizaciones/mias (El Proveedor ve sus cotizaciones y si ganó) ----

    [Fact]
    public async Task GetMisCotizaciones_AislamientoEntreProveedores_ProveedorSoloRecibeLasSuyasYSinDatosDeTerceros()
    {
        var articulo = Guid.NewGuid();
        var licitacion1 = await SembrarLicitacionConCantidadesAsync((articulo, 10m));
        var licitacion2 = await SembrarLicitacionConCantidadesAsync((articulo, 25m));

        var proveedorA = Guid.NewGuid();
        var proveedorB = Guid.NewGuid();
        var client = _factory.CreateClient();

        // Proveedor A cotiza en licitación 1 y licitación 2
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var postA1 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion1.LicitacionId,
            ProveedorId = proveedorA,
            Flete = 300m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, postA1.StatusCode);

        await Task.Delay(20);

        var postA2 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion2.LicitacionId,
            ProveedorId = proveedorA,
            Flete = 500m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 90m } }
        });
        Assert.Equal(HttpStatusCode.Created, postA2.StatusCode);

        // Proveedor B cotiza en licitación 1 con precios distintos
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorB));
        var postB1 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion1.LicitacionId,
            ProveedorId = proveedorB,
            Flete = 50m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 75m } }
        });
        Assert.Equal(HttpStatusCode.Created, postB1.StatusCode);

        // Proveedor A consulta /api/cotizaciones/mias (incluso intentando pasar proveedorId de B en query string)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var respA = await client.GetAsync($"/api/cotizaciones/mias?proveedorId={proveedorB}");
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);

        var rawJsonA = await respA.Content.ReadAsStringAsync();
        using var docA = JsonDocument.Parse(rawJsonA);
        Assert.Equal(2, docA.RootElement.GetArrayLength());
        foreach (var elem in docA.RootElement.EnumerateArray())
        {
            Assert.EndsWith("Z", elem.GetProperty("fechaCreacion").GetString());
            Assert.EndsWith("Z", elem.GetProperty("fechaLimiteCotizacion").GetString());
            // Confidencialidad: no expone puntajeTotal ni esMasBarata de comparativa
            Assert.False(elem.TryGetProperty("puntajeTotal", out _));
            Assert.False(elem.TryGetProperty("esMasBarata", out _));
        }

        var misCotizacionesA = docA.RootElement.Deserialize<List<MiCotizacionResponseDto>>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(misCotizacionesA);
        Assert.Equal(2, misCotizacionesA.Count);
        Assert.All(misCotizacionesA, c => Assert.Equal(proveedorA, c.ProveedorId));
        Assert.DoesNotContain(misCotizacionesA, c => c.ProveedorId == proveedorB);

        // Ordenadas por fecha de creación descendente (la de licitación 2 primero)
        Assert.Equal(licitacion2.LicitacionId, misCotizacionesA[0].LicitacionId);
        Assert.Equal(2250m, misCotizacionesA[0].SubtotalItems); // 25 * 90
        Assert.Equal(500m, misCotizacionesA[0].Flete);
        Assert.Equal(2750m, misCotizacionesA[0].CostoTotal);

        Assert.Equal(licitacion1.LicitacionId, misCotizacionesA[1].LicitacionId);
        Assert.Equal(1000m, misCotizacionesA[1].SubtotalItems); // 10 * 100
        Assert.Equal(300m, misCotizacionesA[1].Flete);
        Assert.Equal(1300m, misCotizacionesA[1].CostoTotal);
    }

    [Fact]
    public async Task GetMisCotizaciones_EstadosYResultadoAdjudicacion_Enviada_Gano_NoGano_YLicitacionCerrada()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionConCantidadesAsync((articulo, 10m));

        var proveedorA = Guid.NewGuid();
        var proveedorB = Guid.NewGuid();
        var client = _factory.CreateClient();

        // 1. Ambos proveedores cargan su cotización mientras la licitación está Abierta
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var postA = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorA,
            Flete = 200m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 100m } }
        });
        Assert.Equal(HttpStatusCode.Created, postA.StatusCode);
        var cotizacionA = await postA.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(cotizacionA);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorB));
        var postB = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorB,
            Flete = 400m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 120m } }
        });
        Assert.Equal(HttpStatusCode.Created, postB.StatusCode);

        // 2. Mientras la licitación está Abierta -> ambos ven estadoLicitacion="Abierta", esGanadora=false, estadoResultado="Enviada"
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var miasAbiertaA = await client.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(miasAbiertaA);
        var itemAbiertaA = Assert.Single(miasAbiertaA);
        Assert.Equal("Abierta", itemAbiertaA.EstadoLicitacion);
        Assert.False(itemAbiertaA.EsGanadora);
        Assert.Equal("Enviada", itemAbiertaA.EstadoResultado);
        Assert.Equal(licitacion.NumeroRequisicion, itemAbiertaA.NumeroRequisicion);
        Assert.Equal(licitacion.ObraId, itemAbiertaA.ObraId);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorB));
        var miasAbiertaB = await client.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(miasAbiertaB);
        var itemAbiertaB = Assert.Single(miasAbiertaB);
        Assert.Equal("Abierta", itemAbiertaB.EstadoLicitacion);
        Assert.False(itemAbiertaB.EsGanadora);
        Assert.Equal("Enviada", itemAbiertaB.EstadoResultado);

        // 3. El Comprador adjudica la licitación a favor del Proveedor A
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var adjudicarResp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/adjudicar",
            new { cotizacionId = cotizacionA.CotizacionId });
        Assert.Equal(HttpStatusCode.OK, adjudicarResp.StatusCode);

        // 4. El Proveedor A consulta /api/cotizaciones/mias y ve estadoLicitacion="Adjudicada", esGanadora=true, estadoResultado="Gano"
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));
        var miasAdjudicadaA = await client.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(miasAdjudicadaA);
        var itemGanadorA = Assert.Single(miasAdjudicadaA);
        Assert.Equal("Adjudicada", itemGanadorA.EstadoLicitacion);
        Assert.True(itemGanadorA.EsGanadora);
        Assert.Equal("Gano", itemGanadorA.EstadoResultado);

        // 5. El Proveedor B consulta /api/cotizaciones/mias y ve estadoLicitacion="Adjudicada", esGanadora=false, estadoResultado="NoGano"
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorB));
        var miasAdjudicadaB = await client.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(miasAdjudicadaB);
        var itemPerdedorB = Assert.Single(miasAdjudicadaB);
        Assert.Equal("Adjudicada", itemPerdedorB.EstadoLicitacion);
        Assert.False(itemPerdedorB.EsGanadora);
        Assert.Equal("NoGano", itemPerdedorB.EstadoResultado);
        Assert.Equal(proveedorB, itemPerdedorB.ProveedorId);
        Assert.Equal(1200m, itemPerdedorB.SubtotalItems);
        Assert.Equal(400m, itemPerdedorB.Flete);
        Assert.Equal(1600m, itemPerdedorB.CostoTotal);

        // 6. Caso licitación Cerrada sin adjudicar -> estadoResultado="LicitacionCerrada"
        var licitacionCerrada = await SembrarLicitacionConCantidadesAsync((articulo, 5m));
        var proveedorC = Guid.NewGuid();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorC));
        var postC = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionCerrada.LicitacionId,
            ProveedorId = proveedorC,
            Flete = 100m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 80m } }
        });
        Assert.Equal(HttpStatusCode.Created, postC.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var licDb = await db.Licitaciones.FirstAsync(l => l.LicitacionId == licitacionCerrada.LicitacionId);
            licDb.Estado = EstadoLicitacion.Cerrada;
            await db.SaveChangesAsync();
        }

        var miasCerradaC = await client.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(miasCerradaC);
        var itemCerradaC = Assert.Single(miasCerradaC);
        Assert.Equal("Cerrada", itemCerradaC.EstadoLicitacion);
        Assert.False(itemCerradaC.EsGanadora);
        Assert.Equal("LicitacionCerrada", itemCerradaC.EstadoResultado);
    }

    [Fact]
    public async Task GetMisCotizaciones_SeguridadRBAC_SinToken401_CompradorYCapataz403_AdminPuedeConsultarPorProveedorId()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionConCantidadesAsync((articulo, 10m));
        var proveedorId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        // El proveedor carga una cotización
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
        var postResp = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 150m,
            PlazoEntregaDias = 4,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 95m } }
        });
        Assert.Equal(HttpStatusCode.Created, postResp.StatusCode);

        // 1. Sin token -> 401 Unauthorized con ProblemDetails + correlationId
        client.DefaultRequestHeaders.Authorization = null;
        var unauthResp = await client.GetAsync("/api/cotizaciones/mias");
        await AssertProblemDetailsWithCorrelationIdAsync(unauthResp, HttpStatusCode.Unauthorized, correlationId);

        // 2. Comprador -> 403 Forbidden con ProblemDetails + correlationId
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var compradorResp = await client.GetAsync("/api/cotizaciones/mias");
        await AssertProblemDetailsWithCorrelationIdAsync(compradorResp, HttpStatusCode.Forbidden, correlationId);

        // 3. Capataz -> 403 Forbidden con ProblemDetails + correlationId
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));
        var capatazResp = await client.GetAsync("/api/cotizaciones/mias");
        await AssertProblemDetailsWithCorrelationIdAsync(capatazResp, HttpStatusCode.Forbidden, correlationId);

        // 4. Admin consultando las cotizaciones de un proveedor específico con ?proveedorId=... -> 200 OK
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin"));
        var adminResp = await client.GetAsync($"/api/cotizaciones/mias?proveedorId={proveedorId}");
        Assert.Equal(HttpStatusCode.OK, adminResp.StatusCode);

        var cotizacionesProveedor = await adminResp.Content.ReadFromJsonAsync<List<MiCotizacionResponseDto>>();
        Assert.NotNull(cotizacionesProveedor);
        var unica = Assert.Single(cotizacionesProveedor);
        Assert.Equal(proveedorId, unica.ProveedorId);
        Assert.Equal(1100m, unica.CostoTotal); // (10 * 95) + 150
    }

    [Fact]
    public async Task PostCotizacion_ProveedorPropio_GuardaElNombreDelTokenYLoDevuelve()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);
        var proveedorId = Guid.NewGuid();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        var response = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 10m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var creada = await response.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.Equal("Usuario Proveedor", creada!.ProveedorNombre);

        // y queda guardado: el listado del Comprador lo devuelve
        var comprador = _factory.CreateClient();
        comprador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var lista = await comprador.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");
        Assert.Equal("Usuario Proveedor", Assert.Single(lista!).ProveedorNombre);
    }

    [Fact]
    public async Task PostCotizacion_AdminEnNombreDeOtroProveedor_NoGuardaElNombreDelAdmin()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin"));

        var response = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = Guid.NewGuid(),
            Flete = 0m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = 10m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var creada = await response.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.Null(creada!.ProveedorNombre);
    }

    [Fact]
    public async Task GetCotizaciones_DevuelveDesgloseRealQueSumaElPuntajeTotal()
    {
        var articulo = Guid.NewGuid();
        var licitacion = await SembrarLicitacionAsync(EstadoLicitacion.Abierta, null, articulo);

        foreach (var (precio, flete, plazo) in new[] { (100m, 500m, 10), (130m, 200m, 4), (115m, 350m, 7) })
        {
            var proveedorId = Guid.NewGuid();
            var c = _factory.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));
            var r = await c.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
            {
                LicitacionId = licitacion.LicitacionId,
                ProveedorId = proveedorId,
                Flete = flete,
                PlazoEntregaDias = plazo,
                Items = new List<CrearCotizacionItemDto> { new() { ArticuloId = articulo, PrecioUnitario = precio } }
            });
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        }

        var comprador = _factory.CreateClient();
        comprador.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var lista = await comprador.GetFromJsonAsync<List<CotizacionResponseDto>>(
            $"/api/cotizaciones?licitacionId={licitacion.LicitacionId}");

        Assert.Equal(3, lista!.Count);
        Assert.All(lista, c =>
        {
            var d = c.DesglosePuntaje!;
            Assert.Equal(c.PuntajeTotal, d.Precio + d.Flete + d.Plazo + d.Calificacion);
            // Sin calificaciones todas valen 0 y empatan: el scoring le da el máximo del criterio (peso 0.1 = 10 puntos) a todas por igual.
            Assert.Equal(10m, d.Calificacion);
        });
        // El desglose ya no es "puntaje × peso": con pesos distintos y ofertas distintas no puede ser proporcional al total.
        Assert.Contains(lista, c => c.DesglosePuntaje!.Precio != Math.Round(c.PuntajeTotal * 0.5m, 2));
    }
}
