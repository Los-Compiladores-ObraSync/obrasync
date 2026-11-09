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

public class LicitacionesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbName;

    public LicitacionesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _dbName = $"DB_Licit_Test_{Guid.NewGuid()}";

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

    private async Task SembrarLicitacionesAsync(params Licitacion[] licitaciones)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        db.Licitaciones.AddRange(licitaciones);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListarLicitaciones_ConRolProveedor_SoloDevuelveLicitacionesEnEstadoAbiertaOrdenadasPorFechaDescendente()
    {
        var obraId = Guid.NewGuid();
        var abiertaMasAntiguaId = Guid.NewGuid();
        var abiertaMasRecienteId = Guid.NewGuid();
        var cerradaId = Guid.NewGuid();
        var adjudicadaId = Guid.NewGuid();

        await SembrarLicitacionesAsync(
            new Licitacion
            {
                LicitacionId = abiertaMasAntiguaId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0001",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Cemento Portland (bolsa 50 kg)",
                        Cantidad = 80m,
                        UnidadMedida = "BOLSA"
                    }
                }
            },
            new Licitacion
            {
                LicitacionId = cerradaId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0002",
                ObraId = obraId,
                Estado = EstadoLicitacion.Cerrada,
                FechaCreacion = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Cal Hidratada",
                        Cantidad = 40m,
                        UnidadMedida = "BOLSA"
                    }
                }
            },
            new Licitacion
            {
                LicitacionId = adjudicadaId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0003",
                ObraId = obraId,
                Estado = EstadoLicitacion.Adjudicada,
                FechaCreacion = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Hierro ADN 420",
                        Cantidad = 100m,
                        UnidadMedida = "UNIDAD"
                    }
                }
            },
            new Licitacion
            {
                LicitacionId = abiertaMasRecienteId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0004",
                ObraId = obraId,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = new DateTime(2026, 11, 4, 8, 30, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Arena Gruesa",
                        Cantidad = 15m,
                        UnidadMedida = "M3"
                    }
                }
            });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));

        // 1. Consulta sin filtro de estado: el Proveedor solo recibe las Abiertas, ordenadas de más nueva a más antigua
        var response = await client.GetAsync($"/api/licitaciones?obraId={obraId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var lista = await response.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(lista);
        Assert.Equal(2, lista.Count);
        Assert.All(lista, l => Assert.Equal("Abierta", l.Estado));
        Assert.Equal(abiertaMasRecienteId, lista[0].LicitacionId);
        Assert.Equal(abiertaMasAntiguaId, lista[1].LicitacionId);
        Assert.Single(lista[0].Items);
        Assert.Single(lista[1].Items);

        // 2. Aunque el Proveedor envíe ?estado=Cerrada en la query string, la regla fuerza estrictamente Estado == Abierta
        var responseForzandoCerrada = await client.GetAsync($"/api/licitaciones?obraId={obraId}&estado=Cerrada");
        Assert.Equal(HttpStatusCode.OK, responseForzandoCerrada.StatusCode);

        var listaForzada = await responseForzandoCerrada.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(listaForzada);
        Assert.Equal(2, listaForzada.Count);
        Assert.All(listaForzada, l => Assert.Equal("Abierta", l.Estado));
    }

    private static Licitacion NuevaLicitacionAbierta(Guid licitacionId, Guid obraId, string numero)
    {
        return new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = numero,
            ObraId = obraId,
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
            FechaLimiteCotizacion = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArticuloId = Guid.NewGuid(),
                    Descripcion = "Cemento",
                    Cantidad = 10m,
                    UnidadMedida = "BOLSA"
                }
            }
        };
    }

    private async Task SembrarCotizacionAsync(Guid licitacionId, Guid proveedorId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var cotizacionId = Guid.NewGuid();
        db.Cotizaciones.Add(new Cotizacion
        {
            CotizacionId = cotizacionId,
            LicitacionId = licitacionId,
            ProveedorId = proveedorId,
            Flete = 100m,
            PlazoEntregaDias = 5,
            FechaCreacion = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc),
            Items = new List<CotizacionItem>
            {
                new() { Id = Guid.NewGuid(), CotizacionId = cotizacionId, ArticuloId = Guid.NewGuid(), PrecioUnitario = 50m }
            }
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListarLicitaciones_ConRolProveedor_MarcaYaCotizadaSoloLasQueCotizoEseProveedor()
    {
        var obraId = Guid.NewGuid();
        var proveedorQueCotizo = Guid.NewGuid();
        var otroProveedor = Guid.NewGuid();
        var cotizadaId = Guid.NewGuid();
        var sinCotizarId = Guid.NewGuid();

        await SembrarLicitacionesAsync(
            NuevaLicitacionAbierta(cotizadaId, obraId, "REQ-2026-0101"),
            NuevaLicitacionAbierta(sinCotizarId, obraId, "REQ-2026-0102"));
        // La del otro proveedor sobre la licitación "sinCotizar" NO debe contar para el primero.
        await SembrarCotizacionAsync(cotizadaId, proveedorQueCotizo);
        await SembrarCotizacionAsync(sinCotizarId, otroProveedor);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorQueCotizo));

        var response = await client.GetAsync($"/api/licitaciones?obraId={obraId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();

        Assert.NotNull(lista);
        Assert.Equal(2, lista.Count);
        Assert.True(lista.Single(l => l.LicitacionId == cotizadaId).YaCotizada);
        Assert.False(lista.Single(l => l.LicitacionId == sinCotizarId).YaCotizada);
    }

    [Fact]
    public async Task ListarLicitaciones_ConRolProveedorQueNoCotizoNada_MarcaTodasComoNoCotizadas()
    {
        var obraId = Guid.NewGuid();
        await SembrarLicitacionesAsync(
            NuevaLicitacionAbierta(Guid.NewGuid(), obraId, "REQ-2026-0111"),
            NuevaLicitacionAbierta(Guid.NewGuid(), obraId, "REQ-2026-0112"));

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));

        var lista = await client.GetFromJsonAsync<List<LicitacionResponseDto>>($"/api/licitaciones?obraId={obraId}");

        Assert.NotNull(lista);
        Assert.Equal(2, lista.Count);
        Assert.All(lista, l => Assert.False(l.YaCotizada));
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task ListarLicitaciones_ConRolCompradorOAdmin_YaCotizadaVieneNull(string rol)
    {
        var obraId = Guid.NewGuid();
        var licitacionId = Guid.NewGuid();
        await SembrarLicitacionesAsync(NuevaLicitacionAbierta(licitacionId, obraId, "REQ-2026-0121"));
        var proveedorId = Guid.NewGuid();
        await SembrarCotizacionAsync(licitacionId, proveedorId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol, proveedorId));

        var lista = await client.GetFromJsonAsync<List<LicitacionResponseDto>>($"/api/licitaciones?obraId={obraId}");

        Assert.NotNull(lista);
        Assert.Single(lista);
        Assert.Null(lista[0].YaCotizada);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task ListarLicitaciones_ConRolCompradorOAdmin_VeTodosLosEstadosYFiltraPorObraIdYEstadoCaseInsensitive(string rol)
    {
        var obra1Id = Guid.NewGuid();
        var obra2Id = Guid.NewGuid();
        var licAbiertaObra1Id = Guid.NewGuid();
        var licCerradaObra1Id = Guid.NewGuid();
        var licAdjudicadaObra2Id = Guid.NewGuid();

        await SembrarLicitacionesAsync(
            new Licitacion
            {
                LicitacionId = licAbiertaObra1Id,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0010",
                ObraId = obra1Id,
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Cemento",
                        Cantidad = 50m,
                        UnidadMedida = "BOLSA"
                    }
                }
            },
            new Licitacion
            {
                LicitacionId = licCerradaObra1Id,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0011",
                ObraId = obra1Id,
                Estado = EstadoLicitacion.Cerrada,
                FechaCreacion = new DateTime(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 8, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Cal",
                        Cantidad = 30m,
                        UnidadMedida = "BOLSA"
                    }
                }
            },
            new Licitacion
            {
                LicitacionId = licAdjudicadaObra2Id,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0012",
                ObraId = obra2Id,
                Estado = EstadoLicitacion.Adjudicada,
                FechaCreacion = new DateTime(2026, 11, 3, 18, 0, 0, DateTimeKind.Utc),
                FechaLimiteCotizacion = new DateTime(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        ArticuloId = Guid.NewGuid(),
                        Descripcion = "Piedra Partida",
                        Cantidad = 12m,
                        UnidadMedida = "M3"
                    }
                }
            });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));

        // 1. Sin filtros: ve todos los estados ordenados por FechaCreacion descendente
        var todasResp = await client.GetAsync("/api/licitaciones");
        Assert.Equal(HttpStatusCode.OK, todasResp.StatusCode);
        var todas = await todasResp.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(todas);

        var deEstasObras = todas.Where(l => l.ObraId == obra1Id || l.ObraId == obra2Id).ToList();
        Assert.Equal(3, deEstasObras.Count);
        Assert.Equal(licAdjudicadaObra2Id, deEstasObras[0].LicitacionId);
        Assert.Equal(licCerradaObra1Id, deEstasObras[1].LicitacionId);
        Assert.Equal(licAbiertaObra1Id, deEstasObras[2].LicitacionId);

        // 2. Filtro por obraId
        var porObraResp = await client.GetAsync($"/api/licitaciones?obraId={obra1Id}");
        var porObra = await porObraResp.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(porObra);
        Assert.Equal(2, porObra.Count);
        Assert.Equal(licCerradaObra1Id, porObra[0].LicitacionId);
        Assert.Equal(licAbiertaObra1Id, porObra[1].LicitacionId);

        // 3. Filtro por estado case-insensitive ("cerrada" y "ADJUDICADA")
        var cerradaMinusResp = await client.GetAsync($"/api/licitaciones?obraId={obra1Id}&estado=cerrada");
        var cerradaMinus = await cerradaMinusResp.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(cerradaMinus);
        var unicaCerrada = Assert.Single(cerradaMinus);
        Assert.Equal(licCerradaObra1Id, unicaCerrada.LicitacionId);
        Assert.Equal("Cerrada", unicaCerrada.Estado);

        var adjudicadaMayusResp = await client.GetAsync($"/api/licitaciones?obraId={obra2Id}&estado=ADJUDICADA");
        var adjudicadaMayus = await adjudicadaMayusResp.Content.ReadFromJsonAsync<List<LicitacionResponseDto>>();
        Assert.NotNull(adjudicadaMayus);
        var unicaAdjudicada = Assert.Single(adjudicadaMayus);
        Assert.Equal(licAdjudicadaObra2Id, unicaAdjudicada.LicitacionId);
        Assert.Equal("Adjudicada", unicaAdjudicada.Estado);
    }

    [Fact]
    public async Task ObtenerPorId_ExistenteDevuelveDetalleCompleto_InexistenteDevuelve404ProblemDetailsConCorrelationId()
    {
        var licitacionId = Guid.NewGuid();
        var requisicionId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var correlationId = Guid.NewGuid().ToString();

        await SembrarLicitacionesAsync(new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = requisicionId,
            NumeroRequisicion = "REQ-2026-0050",
            ObraId = obraId,
            Estado = EstadoLicitacion.Abierta,
            FechaCreacion = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc),
            FechaLimiteCotizacion = new DateTime(2026, 11, 9, 18, 0, 0, DateTimeKind.Utc),
            Items = new List<LicitacionItem>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    ArticuloId = articuloId,
                    Descripcion = "Cemento Portland",
                    Cantidad = 120m,
                    UnidadMedida = "BOLSA"
                }
            }
        });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor"));

        // 1. Consulta por ID existente -> 200 OK con todos los datos e ítems
        var okResp = await client.GetAsync($"/api/licitaciones/{licitacionId}");
        Assert.Equal(HttpStatusCode.OK, okResp.StatusCode);

        var detalle = await okResp.Content.ReadFromJsonAsync<LicitacionResponseDto>();
        Assert.NotNull(detalle);
        Assert.Equal(licitacionId, detalle.LicitacionId);
        Assert.Equal(requisicionId, detalle.RequisicionId);
        Assert.Equal("REQ-2026-0050", detalle.NumeroRequisicion);
        Assert.Equal(obraId, detalle.ObraId);
        Assert.Equal("Abierta", detalle.Estado);
        var item = Assert.Single(detalle.Items);
        Assert.Equal(articuloId, item.ArticuloId);
        Assert.Equal("Cemento Portland", item.Descripcion);
        Assert.Equal(120m, item.Cantidad);
        Assert.Equal("BOLSA", item.UnidadMedida);

        // 2. Consulta por ID inexistente -> 404 Not Found con ProblemDetails y correlationId
        var notFoundResp = await client.GetAsync($"/api/licitaciones/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        Assert.Equal("application/problem+json", notFoundResp.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await notFoundResp.Content.ReadAsStringAsync());
        Assert.Equal(404, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(correlationId, doc.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task EndpointsLicitaciones_SinTokenDevuelve401_ConRolCapatazDevuelve403ConProblemDetailsYCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        // 1. Sin token -> 401 Unauthorized con ProblemDetails + correlationId
        var unauthResp = await client.GetAsync("/api/licitaciones");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);
        Assert.Equal("application/problem+json", unauthResp.Content.Headers.ContentType?.MediaType);

        using (var doc = JsonDocument.Parse(await unauthResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(correlationId, doc.RootElement.GetProperty("correlationId").GetString());
        }

        // 2. Rol Capataz -> 403 Forbidden con ProblemDetails + correlationId
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Capataz"));

        var forbiddenListResp = await client.GetAsync("/api/licitaciones");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenListResp.StatusCode);
        Assert.Equal("application/problem+json", forbiddenListResp.Content.Headers.ContentType?.MediaType);

        using (var doc = JsonDocument.Parse(await forbiddenListResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(correlationId, doc.RootElement.GetProperty("correlationId").GetString());
        }

        var forbiddenByIdResp = await client.GetAsync($"/api/licitaciones/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenByIdResp.StatusCode);
        Assert.Equal("application/problem+json", forbiddenByIdResp.Content.Headers.ContentType?.MediaType);
    }
}
