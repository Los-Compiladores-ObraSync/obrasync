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
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class PaginacionLicitacionesCotizacionesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public PaginacionLicitacionesCotizacionesTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Paginacion_{Guid.NewGuid()}";

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
        Assert.Equal("Paginación inválida", root.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GetLicitaciones_PrimeraYUltimaPagina_DevuelveItemsYCabecerasDePaginacion()
    {
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var fechaBase = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        using var factory = CreateIsolatedFactory(db =>
        {
            for (int i = 1; i <= 5; i++)
            {
                db.Licitaciones.Add(new Licitacion
                {
                    LicitacionId = Guid.NewGuid(),
                    RequisicionId = Guid.NewGuid(),
                    NumeroRequisicion = $"REQ-2026-000{i}",
                    ObraId = obraId,
                    Estado = EstadoLicitacion.Abierta,
                    FechaCreacion = fechaBase.AddMinutes(i),
                    FechaLimiteCotizacion = fechaBase.AddDays(5),
                    Items = new List<LicitacionItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            ArticuloId = articuloId,
                            Descripcion = "Cemento Portland",
                            Cantidad = 10m * i,
                            UnidadMedida = "BOLSA"
                        }
                    }
                });
            }
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // Página 1 (tamano=2): 2 ítems (REQ-2026-0005 y REQ-2026-0004), X-Total-Count=5
        var respP1 = await client.GetAsync("/api/licitaciones?pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP1.StatusCode);
        Assert.Equal("5", respP1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", respP1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP1.Headers.GetValues("X-Page-Size").Single());

        var p1 = await respP1.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(p1);
        Assert.Equal(2, p1.Count);
        Assert.Equal("REQ-2026-0005", p1[0].NumeroRequisicion);
        Assert.Equal("REQ-2026-0004", p1[1].NumeroRequisicion);

        // Última página (pagina=3, tamano=2): 1 ítem (REQ-2026-0001), X-Total-Count=5
        var respP3 = await client.GetAsync("/api/licitaciones?pagina=3&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP3.StatusCode);
        Assert.Equal("5", respP3.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("3", respP3.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP3.Headers.GetValues("X-Page-Size").Single());

        var p3 = await respP3.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(p3);
        Assert.Single(p3);
        Assert.Equal("REQ-2026-0001", p3[0].NumeroRequisicion);
    }

    [Fact]
    public async Task GetLicitaciones_ConMismaFechaCreacion_DesempataPorIdSinDuplicarNiPerderFilas()
    {
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var fechaIdentica = new DateTime(2026, 11, 5, 14, 0, 0, DateTimeKind.Utc);
        var idsEsperados = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000010"),
            Guid.Parse("00000000-0000-0000-0000-000000000020"),
            Guid.Parse("00000000-0000-0000-0000-000000000030"),
            Guid.Parse("00000000-0000-0000-0000-000000000040")
        };

        using var factory = CreateIsolatedFactory(db =>
        {
            foreach (var id in new[] { idsEsperados[3], idsEsperados[1], idsEsperados[0], idsEsperados[2] })
            {
                db.Licitaciones.Add(new Licitacion
                {
                    LicitacionId = id,
                    RequisicionId = Guid.NewGuid(),
                    NumeroRequisicion = $"REQ-{id.ToString()[..8]}",
                    ObraId = obraId,
                    Estado = EstadoLicitacion.Abierta,
                    FechaCreacion = fechaIdentica,
                    FechaLimiteCotizacion = fechaIdentica.AddDays(5),
                    Items = new List<LicitacionItem>
                    {
                        new()
                        {
                            Id = Guid.NewGuid(),
                            ArticuloId = articuloId,
                            Descripcion = "Cemento",
                            Cantidad = 20m,
                            UnidadMedida = "BOLSA"
                        }
                    }
                });
            }
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var p1 = await client.GetFromJsonAsync<List<LicitacionResponseDto>>("/api/licitaciones?pagina=1&tamano=2");
        var p2 = await client.GetFromJsonAsync<List<LicitacionResponseDto>>("/api/licitaciones?pagina=2&tamano=2");

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        var recibidos = p1.Concat(p2).Select(l => l.LicitacionId).ToList();
        Assert.Equal(idsEsperados, recibidos);
    }

    [Fact]
    public async Task GetLicitaciones_XTotalCount_RespetaFiltrosDeObraEstadoYRolProveedor()
    {
        var obraA = Guid.NewGuid();
        var obraB = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            // Obra A: 2 Abiertas, 1 Cerrada. Obra B: 1 Adjudicada
            db.Licitaciones.AddRange(
                CrearEntidadLicitacion(obraA, EstadoLicitacion.Abierta, "REQ-A1", articuloId),
                CrearEntidadLicitacion(obraA, EstadoLicitacion.Abierta, "REQ-A2", articuloId),
                CrearEntidadLicitacion(obraA, EstadoLicitacion.Cerrada, "REQ-A3", articuloId),
                CrearEntidadLicitacion(obraB, EstadoLicitacion.Adjudicada, "REQ-B1", articuloId));
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var respObraA = await clientComprador.GetAsync($"/api/licitaciones?obraId={obraA}&pagina=1&tamano=1");
        Assert.Equal("3", respObraA.Headers.GetValues("X-Total-Count").Single());

        var respObraACerrada = await clientComprador.GetAsync($"/api/licitaciones?obraId={obraA}&estado=Cerrada&pagina=1&tamano=1");
        Assert.Equal("1", respObraACerrada.Headers.GetValues("X-Total-Count").Single());

        // Rol Proveedor solo puede ver Abiertas -> X-Total-Count = 2
        var clientProveedor = factory.CreateClient();
        clientProveedor.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));

        var respProv = await clientProveedor.GetAsync("/api/licitaciones?pagina=1&tamano=1");
        Assert.Equal("2", respProv.Headers.GetValues("X-Total-Count").Single());
    }

    [Fact]
    public async Task GetCotizaciones_PaginacionYPreservacionDeScoringYEsMasBarataSobreTodasLasOfertas()
    {
        var articuloId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var otraLicitacionId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var fechaBase = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

        var provCostosaReciente = Guid.NewGuid();
        var provIntermediaReciente = Guid.NewGuid();
        var provMasBarataYMejorAntigua = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            // Licitación principal: 10 bolsas de cemento
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0500",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = fechaBase.AddHours(-1),
                FechaLimiteCotizacion = fechaBase.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 10m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            // Otra licitación con 2 cotizaciones (para probar que X-Total-Count filtra por licitacionId)
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = otraLicitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0501",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = fechaBase.AddHours(-1),
                FechaLimiteCotizacion = fechaBase.AddDays(5),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = articuloId,
                        Descripcion = "Cemento Portland",
                        Cantidad = 10m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });

            // Oferta 1 (más antigua -> caerá en la página 2 cuando pidamos pagina=1&tamano=2):
            // Es LA MÁS BARATA y LA DE MEJOR PLAZO (ganadora real de toda la licitación):
            // 10 * $100 = $1000 + $50 flete = $1050 costoTotal, plazo 1 día
            var cot1Id = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cot1Id,
                LicitacionId = licitacionId,
                ProveedorId = provMasBarataYMejorAntigua,
                Flete = 50m,
                PlazoEntregaDias = 1,
                FechaCreacion = fechaBase.AddMinutes(1),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cot1Id, ArticuloId = articuloId, PrecioUnitario = 100m }
                }
            });

            // Oferta 2 (intermedia -> cae en página 1):
            // 10 * $200 = $2000 + $200 flete = $2200 costoTotal, plazo 5 días
            var cot2Id = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cot2Id,
                LicitacionId = licitacionId,
                ProveedorId = provIntermediaReciente,
                Flete = 200m,
                PlazoEntregaDias = 5,
                FechaCreacion = fechaBase.AddMinutes(2),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cot2Id, ArticuloId = articuloId, PrecioUnitario = 200m }
                }
            });

            // Oferta 3 (más reciente -> cae en página 1):
            // 10 * $300 = $3000 + $300 flete = $3300 costoTotal, plazo 10 días
            var cot3Id = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cot3Id,
                LicitacionId = licitacionId,
                ProveedorId = provCostosaReciente,
                Flete = 300m,
                PlazoEntregaDias = 10,
                FechaCreacion = fechaBase.AddMinutes(3),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cot3Id, ArticuloId = articuloId, PrecioUnitario = 300m }
                }
            });

            // Cotización en otraLicitacionId
            var cotOtraId = Guid.NewGuid();
            db.Cotizaciones.Add(new Cotizacion
            {
                CotizacionId = cotOtraId,
                LicitacionId = otraLicitacionId,
                ProveedorId = Guid.NewGuid(),
                Flete = 100m,
                PlazoEntregaDias = 3,
                FechaCreacion = fechaBase.AddMinutes(4),
                Items = new List<CotizacionItem>
                {
                    new() { Id = Guid.NewGuid(), CotizacionId = cotOtraId, ArticuloId = articuloId, PrecioUnitario = 150m }
                }
            });
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // 1. Primera página (pagina=1&tamano=2): devuelve las 2 más recientes (Oferta 3 y Oferta 2)
        // PERO ninguna de las 2 debe aparecer como EsMasBarata ni EsGanadora, porque la Oferta 1 (en página 2) es más barata y ganadora!
        var respP1 = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}&pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP1.StatusCode);
        Assert.Equal("3", respP1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", respP1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP1.Headers.GetValues("X-Page-Size").Single());

        var p1 = await respP1.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(p1);
        Assert.Equal(2, p1.Count);
        Assert.Equal(provCostosaReciente, p1[0].ProveedorId);
        Assert.Equal(provIntermediaReciente, p1[1].ProveedorId);
        Assert.All(p1, c =>
        {
            Assert.False(c.EsMasBarata);
            Assert.False(c.EsGanadora);
        });

        // 2. Segunda (última) página (pagina=2&tamano=2): devuelve 1 fila (Oferta 1) con EsMasBarata=true y EsGanadora=true
        var respP2 = await client.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}&pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP2.StatusCode);
        Assert.Equal("3", respP2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", respP2.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP2.Headers.GetValues("X-Page-Size").Single());

        var p2 = await respP2.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(p2);
        var unicaP2 = Assert.Single(p2);
        Assert.Equal(provMasBarataYMejorAntigua, unicaP2.ProveedorId);
        Assert.Equal(1050m, unicaP2.CostoTotal);
        Assert.True(unicaP2.EsMasBarata);
        Assert.True(unicaP2.EsGanadora);
    }

    [Fact]
    public async Task GetCotizaciones_ConMismaFechaCreacion_DesempataPorIdSinDuplicarNiPerderFilas()
    {
        var articuloId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        var fechaIdentica = new DateTime(2026, 11, 5, 16, 0, 0, DateTimeKind.Utc);
        var idsEsperados = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000201"),
            Guid.Parse("00000000-0000-0000-0000-000000000202"),
            Guid.Parse("00000000-0000-0000-0000-000000000203"),
            Guid.Parse("00000000-0000-0000-0000-000000000204")
        };

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearEntidadLicitacion(Guid.NewGuid(), EstadoLicitacion.Abierta, "REQ-TIE-COT", articuloId, licitacionId));

            foreach (var cotId in new[] { idsEsperados[2], idsEsperados[0], idsEsperados[3], idsEsperados[1] })
            {
                db.Cotizaciones.Add(new Cotizacion
                {
                    CotizacionId = cotId,
                    LicitacionId = licitacionId,
                    ProveedorId = Guid.NewGuid(),
                    Flete = 100m,
                    PlazoEntregaDias = 3,
                    FechaCreacion = fechaIdentica,
                    Items = new List<CotizacionItem>
                    {
                        new() { Id = Guid.NewGuid(), CotizacionId = cotId, ArticuloId = articuloId, PrecioUnitario = 90m }
                    }
                });
            }
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var p1 = await client.GetFromJsonAsync<List<CotizacionResponseDto>>($"/api/cotizaciones?licitacionId={licitacionId}&pagina=1&tamano=2");
        var p2 = await client.GetFromJsonAsync<List<CotizacionResponseDto>>($"/api/cotizaciones?licitacionId={licitacionId}&pagina=2&tamano=2");

        Assert.NotNull(p1);
        Assert.NotNull(p2);
        var recibidos = p1.Concat(p2).Select(c => c.CotizacionId).ToList();
        Assert.Equal(idsEsperados, recibidos);
    }

    [Fact]
    public async Task GetMisCotizaciones_PrimeraYUltimaPagina_OrdenEstableYFiltroPorProveedor()
    {
        var articuloId = Guid.NewGuid();
        var proveedorA = Guid.NewGuid();
        var proveedorB = Guid.NewGuid();
        var fechaIdentica = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

        var idsCotizacionesA = new[]
        {
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            Guid.Parse("00000000-0000-0000-0000-000000000102"),
            Guid.Parse("00000000-0000-0000-0000-000000000103")
        };

        using var factory = CreateIsolatedFactory(db =>
        {
            // Insertamos en orden desordenado pero con la misma FechaCreacion para probar el desempate por Id
            foreach (var cotId in new[] { idsCotizacionesA[2], idsCotizacionesA[0], idsCotizacionesA[1] })
            {
                var licId = Guid.NewGuid();
                db.Licitaciones.Add(CrearEntidadLicitacion(Guid.NewGuid(), EstadoLicitacion.Abierta, $"REQ-{cotId.ToString()[..8]}", articuloId, licId));
                db.Cotizaciones.Add(new Cotizacion
                {
                    CotizacionId = cotId,
                    LicitacionId = licId,
                    ProveedorId = proveedorA,
                    Flete = 100m,
                    PlazoEntregaDias = 3,
                    FechaCreacion = fechaIdentica,
                    Items = new List<CotizacionItem>
                    {
                        new() { Id = Guid.NewGuid(), CotizacionId = cotId, ArticuloId = articuloId, PrecioUnitario = 80m }
                    }
                });
            }

            // 2 cotizaciones de otro proveedor (proveedorB) que NO deben sumarse al X-Total-Count de proveedorA
            for (int i = 0; i < 2; i++)
            {
                var licId = Guid.NewGuid();
                var cotId = Guid.NewGuid();
                db.Licitaciones.Add(CrearEntidadLicitacion(Guid.NewGuid(), EstadoLicitacion.Abierta, $"REQ-B-{i}", articuloId, licId));
                db.Cotizaciones.Add(new Cotizacion
                {
                    CotizacionId = cotId,
                    LicitacionId = licId,
                    ProveedorId = proveedorB,
                    Flete = 50m,
                    PlazoEntregaDias = 2,
                    FechaCreacion = fechaIdentica,
                    Items = new List<CotizacionItem>
                    {
                        new() { Id = Guid.NewGuid(), CotizacionId = cotId, ArticuloId = articuloId, PrecioUnitario = 70m }
                    }
                });
            }
        });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorA));

        // Página 1 (pagina=1&tamano=2): 2 ítems de proveedorA, X-Total-Count=3
        var respP1 = await client.GetAsync("/api/cotizaciones/mias?pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP1.StatusCode);
        Assert.Equal("3", respP1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", respP1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP1.Headers.GetValues("X-Page-Size").Single());

        var p1 = await respP1.Content.ReadFromJsonAsync<List<MiCotizacionResponseDto>>();
        Assert.NotNull(p1);
        Assert.Equal(2, p1.Count);

        // Página 2 (pagina=2&tamano=2): 1 ítem restante de proveedorA, X-Total-Count=3
        var respP2 = await client.GetAsync("/api/cotizaciones/mias?pagina=2&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respP2.StatusCode);
        Assert.Equal("3", respP2.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("2", respP2.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respP2.Headers.GetValues("X-Page-Size").Single());

        var p2 = await respP2.Content.ReadFromJsonAsync<List<MiCotizacionResponseDto>>();
        Assert.NotNull(p2);
        Assert.Single(p2);

        // Orden estable sin repetir ni perder filas
        var idsRecibidos = p1.Concat(p2).Select(c => c.CotizacionId).ToList();
        Assert.Equal(idsCotizacionesA, idsRecibidos);
    }

    [Theory]
    [InlineData("/api/licitaciones?tamano=51")]
    [InlineData("/api/licitaciones?pagina=0")]
    [InlineData("/api/licitaciones?tamano=0")]
    [InlineData("/api/cotizaciones?licitacionId=11111111-1111-1111-1111-111111111111&tamano=51")]
    [InlineData("/api/cotizaciones?licitacionId=11111111-1111-1111-1111-111111111111&pagina=0")]
    [InlineData("/api/cotizaciones?licitacionId=11111111-1111-1111-1111-111111111111&tamano=0")]
    [InlineData("/api/cotizaciones/mias?tamano=51")]
    [InlineData("/api/cotizaciones/mias?pagina=0")]
    [InlineData("/api/cotizaciones/mias?tamano=0")]
    public async Task EndpointsPaginados_ParametrosFueraDeRango_Retornan400ProblemDetailsConCorrelationId(string url)
    {
        using var factory = CreateIsolatedFactory();
        var correlationId = Guid.NewGuid().ToString();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin"));

        var response = await client.GetAsync(url);
        await AssertProblemDetails400Async(response, correlationId);
    }

    private static Licitacion CrearEntidadLicitacion(
        Guid obraId,
        EstadoLicitacion estado,
        string numeroRequisicion,
        Guid articuloId,
        Guid? licitacionId = null)
    {
        return new Licitacion
        {
            LicitacionId = licitacionId ?? Guid.NewGuid(),
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = numeroRequisicion,
            ObraId = obraId,
            Estado = estado,
            FechaCreacion = DateTime.UtcNow,
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArticuloId = articuloId,
                    Descripcion = "Cemento",
                    Cantidad = 10m,
                    UnidadMedida = "BOLSA"
                }
            }
        };
    }
}
