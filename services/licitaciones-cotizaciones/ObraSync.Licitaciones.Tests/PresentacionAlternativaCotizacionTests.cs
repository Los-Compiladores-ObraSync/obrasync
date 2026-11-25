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

public class PresentacionAlternativaCotizacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PresentacionAlternativaCotizacionTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_PresAlt_{Guid.NewGuid()}";

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

    private static async Task AssertProblemDetails400Async(HttpResponseMessage response, string expectedCorrelationId)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task EquivalenciaPresentacionAlternativa_1x50kgVs2x25kgAlMismoPrecioPorKg_MismoSubtotalCostoTotalYPuntajeScoring()
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
                NumeroRequisicion = "REQ-2026-1901",
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
                        Descripcion = "Cemento Portland (bolsa 50 kg)",
                        Cantidad = 1m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });
        });

        var proveedor1Id = Guid.NewGuid();
        var proveedor2Id = Guid.NewGuid();
        var client = factory.CreateClient();

        // 1. Proveedor 1 cotiza presentación estándar del pliego: 1 x 50 kg a $10.000 (sin PresentacionOfrecida ni CantidadOfrecida)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor1Id));

        var post1 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedor1Id,
            Flete = 0m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 10000m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post1.StatusCode);
        var creada1 = await post1.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(creada1);
        Assert.Equal(10000m, creada1.SubtotalItems);
        Assert.Equal(10000m, creada1.CostoTotal);
        Assert.False(creada1.CoberturaParcial);
        Assert.Null(creada1.Items[0].PresentacionOfrecida);
        Assert.Null(creada1.Items[0].CantidadOfrecida);
        Assert.Equal(10000m, creada1.Items[0].SubtotalItem);
        Assert.False(creada1.Items[0].CoberturaParcial);

        // 2. Proveedor 2 cotiza presentación alternativa: 2 x "bolsa 25 kg" a $5.000 c/u (total = 2 * 5000 = $10.000)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedor2Id));

        var post2 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedor2Id,
            Flete = 0m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5000m,
                    PresentacionOfrecida = "bolsa 25 kg",
                    CantidadOfrecida = 2m
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, post2.StatusCode);
        var creada2 = await post2.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(creada2);
        Assert.Equal(10000m, creada2.SubtotalItems);
        Assert.Equal(10000m, creada2.CostoTotal);
        Assert.False(creada2.CoberturaParcial);
        Assert.Equal("bolsa 25 kg", creada2.Items[0].PresentacionOfrecida);
        Assert.Equal(2m, creada2.Items[0].CantidadOfrecida);
        Assert.Equal(10000m, creada2.Items[0].SubtotalItem);
        Assert.False(creada2.Items[0].CoberturaParcial);

        // 3. Al consultar la matriz comparativa GET /api/cotizaciones, ambas tienen el mismo CostoTotal y el mismo PuntajeTotal
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var comparativa = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(comparativa);
        Assert.Equal(2, comparativa.Count);

        var ofertaEstandar = Assert.Single(comparativa, c => c.ProveedorId == proveedor1Id);
        var ofertaAlternativa = Assert.Single(comparativa, c => c.ProveedorId == proveedor2Id);

        Assert.Equal(10000m, ofertaEstandar.SubtotalItems);
        Assert.Equal(10000m, ofertaAlternativa.SubtotalItems);
        Assert.Equal(10000m, ofertaEstandar.CostoTotal);
        Assert.Equal(10000m, ofertaAlternativa.CostoTotal);
        Assert.Equal(ofertaEstandar.PuntajeTotal, ofertaAlternativa.PuntajeTotal);
        Assert.True(ofertaEstandar.EsMasBarata);
        Assert.True(ofertaAlternativa.EsMasBarata);
    }

    [Fact]
    public async Task CoberturaParcial_SeGuardaYExponeConFlagTrue_PeroNoRobaEsMasBarataNiEsGanadoraAOfertasCompletas()
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
                NumeroRequisicion = "REQ-2026-1902",
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
                        Descripcion = "Cemento Portland (bolsa 50 kg)",
                        Cantidad = 1m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });
        });

        var provCompleta1 = Guid.NewGuid();
        var provCompleta2 = Guid.NewGuid();
        var provParcial = Guid.NewGuid();
        var client = factory.CreateClient();

        // Oferta Completa 1: 1 x 50 kg = $10.000
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provCompleta1));
        await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = provCompleta1,
            Flete = 0m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articuloId, PrecioUnitario = 10000m }
            }
        });

        // Oferta Completa 2: 2 x 25 kg a $5.200 = $10.400
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provCompleta2));
        await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = provCompleta2,
            Flete = 0m,
            PlazoEntregaDias = 5,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5200m,
                    PresentacionOfrecida = "bolsa 25 kg",
                    CantidadOfrecida = 2m
                }
            }
        });

        // Oferta con Cobertura Parcial: ofrece SOLO 1 bolsa de 25 kg a $5.000 (cubre 25 kg de los 50 kg pedidos)
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", provParcial));
        var respParcial = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = provParcial,
            Flete = 0m,
            PlazoEntregaDias = 1,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5000m,
                    PresentacionOfrecida = "bolsa 25 kg",
                    CantidadOfrecida = 1m
                }
            }
        });

        // No se descarta sola: responde 201 Created con CoberturaParcial = true
        Assert.Equal(HttpStatusCode.Created, respParcial.StatusCode);
        var dtoParcialCreada = await respParcial.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(dtoParcialCreada);
        Assert.True(dtoParcialCreada.CoberturaParcial);
        Assert.True(dtoParcialCreada.Items[0].CoberturaParcial);

        // En la comparativa aparece con CoberturaParcial = true, pero NO roba EsMasBarata ni EsGanadora
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));
        var getResp = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}");
        var lista = await getResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(lista);
        Assert.Equal(3, lista.Count);

        var itemParcial = Assert.Single(lista, c => c.ProveedorId == provParcial);
        Assert.True(itemParcial.CoberturaParcial);
        Assert.Equal(5000m, itemParcial.CostoTotal);
        Assert.False(itemParcial.EsMasBarata);
        Assert.False(itemParcial.EsGanadora);

        var itemGanadorCompleto = Assert.Single(lista, c => c.ProveedorId == provCompleta1);
        Assert.False(itemGanadorCompleto.CoberturaParcial);
        Assert.True(itemGanadorCompleto.EsMasBarata);
        Assert.True(itemGanadorCompleto.EsGanadora);
    }

    [Fact]
    public async Task Validaciones_PresentacionMayorA100CaracteresOCantidadOfrecidaNoPositiva_Devuelven400ProblemDetailsConCorrelationId()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-1903",
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
                        Cantidad = 10m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorId));

        // 1. PresentacionOfrecida > 100 caracteres (101 chars) -> 400 Bad Request ProblemDetails
        var presentacion101 = new string('A', 101);
        var resp101 = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5000m,
                    PresentacionOfrecida = presentacion101,
                    CantidadOfrecida = 20m
                }
            }
        });
        await AssertProblemDetails400Async(resp101, correlationId);

        // 2. CantidadOfrecida = 0 -> 400 Bad Request ProblemDetails
        var respCantidadCero = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5000m,
                    PresentacionOfrecida = "bolsa 25 kg",
                    CantidadOfrecida = 0m
                }
            }
        });
        await AssertProblemDetails400Async(respCantidadCero, correlationId);

        // 3. CantidadOfrecida < 0 -> 400 Bad Request ProblemDetails
        var respCantidadNegativa = await client.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new()
                {
                    ArticuloId = articuloId,
                    PrecioUnitario = 5000m,
                    PresentacionOfrecida = "bolsa 25 kg",
                    CantidadOfrecida = -2m
                }
            }
        });
        await AssertProblemDetails400Async(respCantidadNegativa, correlationId);
    }

    [Fact]
    public async Task CotizarConMagicLink_AceptaPresentacionAlternativaYCantidadOfrecida_YPersisteEnBase()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var rawToken = "token-magic-link-presentacion-alternativa";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-1904",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = itemPliegoId,
                        LicitacionId = licitacionId,
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland (bolsa 50 kg)",
                        Cantidad = 1m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(rawToken),
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                ProveedorNombre = "Corralón del Sur S.A.",
                FechaDeVencimiento = DateTime.UtcNow.AddHours(2),
                FechaDeUso = null
            });
        });

        var clientAnonimo = factory.CreateClient();
        var response = await clientAnonimo.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{rawToken}/cotizaciones",
            new
            {
                flete = 500m,
                plazoEntregaDias = 4,
                items = new[]
                {
                    new
                    {
                        licitacionItemId = itemPliegoId,
                        precioUnitario = 5000m,
                        presentacionOfrecida = "bolsa 25 kg",
                        cantidadOfrecida = 2m
                    }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(10000m, dto.SubtotalItems);
        Assert.Equal(10500m, dto.CostoTotal);
        Assert.False(dto.CoberturaParcial);
        Assert.Equal("bolsa 25 kg", dto.Items[0].PresentacionOfrecida);
        Assert.Equal(2m, dto.Items[0].CantidadOfrecida);
        Assert.Equal(10000m, dto.Items[0].SubtotalItem);
        Assert.False(dto.Items[0].CoberturaParcial);
    }
}
