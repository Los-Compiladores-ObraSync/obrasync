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

public class CerrarLicitacionEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public CerrarLicitacionEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(Action<LicitacionesDbContext>? seedAction = null)
    {
        var dbName = $"DB_Licit_Cerrar_{Guid.NewGuid()}";

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

    private static Licitacion CrearLicitacion(
        EstadoLicitacion estado = EstadoLicitacion.Abierta,
        Guid? licitacionId = null,
        params Guid[] articulosIds)
    {
        var id = licitacionId ?? Guid.NewGuid();
        var ids = articulosIds.Length > 0 ? articulosIds : new[] { Guid.NewGuid() };

        return new Licitacion
        {
            LicitacionId = id,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = $"REQ-2026-{Random.Shared.Next(1000, 9999)}",
            ObraId = Guid.NewGuid(),
            Estado = estado,
            FechaCreacion = DateTime.UtcNow.AddHours(-2),
            FechaLimiteCotizacion = DateTime.UtcNow.AddDays(5),
            Items = ids.Select((artId, index) => new LicitacionItem
            {
                Id = Guid.NewGuid(),
                LicitacionId = id,
                ArticuloId = artId,
                Descripcion = $"Material {index + 1}",
                Cantidad = 10m,
                UnidadMedida = "BOLSA"
            }).ToList()
        };
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
    public async Task Cerrar_SinToken_Responde401ConProblemDetails()
    {
        using var factory = CreateIsolatedFactory();
        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{Guid.NewGuid()}/cierres",
            new { motivo = "Obra cancelada" });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Unauthorized, cid);
    }

    [Theory]
    [InlineData("Proveedor")]
    [InlineData("Capataz")]
    public async Task Cerrar_ConRolNoAutorizado_Responde403ConProblemDetails(string rol)
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = "Intento no autorizado" });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Forbidden, cid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Abierta, persistida.Estado);
    }

    [Theory]
    [InlineData("Comprador")]
    [InlineData("Admin")]
    public async Task Cerrar_LicitacionAbiertaConCompradorOAdmin_Retorna200SinOrdenCompra(string rol)
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt(rol));

        var antesUtc = DateTime.UtcNow.AddSeconds(-2);
        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = "  Sin ofertas válidas en el plazo esperado  " });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.EndsWith("Z", doc.RootElement.GetProperty("fechaCierre").GetString());

        var dto = doc.RootElement.Deserialize<LicitacionResponseDto>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(dto);
        Assert.Equal(licitacion.LicitacionId, dto.LicitacionId);
        Assert.Equal("Cerrada", dto.Estado);
        Assert.Equal("Sin ofertas válidas en el plazo esperado", dto.MotivoCierre);
        Assert.NotNull(dto.FechaCierre);
        Assert.InRange(dto.FechaCierre!.Value, antesUtc, DateTime.UtcNow.AddSeconds(2));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Cerrada, persistida.Estado);
        Assert.Equal("Sin ofertas válidas en el plazo esperado", persistida.MotivoCierre);
        Assert.NotNull(persistida.FechaCierre);
        Assert.Null(persistida.FechaAdjudicacion);
        Assert.Equal(0, await db.OrdenesCompra.CountAsync());
        Assert.Equal(0, await db.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
    }

    [Fact]
    public async Task Cerrar_LicitacionInexistente_Retorna404ProblemDetails()
    {
        using var factory = CreateIsolatedFactory();
        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{Guid.NewGuid()}/cierres",
            new { motivo = "No existe" });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.NotFound, cid);
    }

    [Fact]
    public async Task Cerrar_LicitacionYaAdjudicada_Retorna409ProblemDetails()
    {
        var licitacion = CrearLicitacion(EstadoLicitacion.Adjudicada);
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = "Intento sobre adjudicada" });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Conflict, cid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Adjudicada, persistida.Estado);
        Assert.Null(persistida.MotivoCierre);
        Assert.Null(persistida.FechaCierre);
        Assert.Null(persistida.FechaAdjudicacion);
    }

    [Fact]
    public async Task Cerrar_LicitacionYaCerrada_Retorna409YConservaMotivoOriginal()
    {
        var fechaOriginal = DateTime.UtcNow.AddDays(-1);
        var licitacion = CrearLicitacion(EstadoLicitacion.Cerrada);
        licitacion.MotivoCierre = "Motivo original";
        licitacion.FechaCierre = fechaOriginal;

        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Admin"));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = "Segundo cierre" });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.Conflict, cid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Cerrada, persistida.Estado);
        Assert.Equal("Motivo original", persistida.MotivoCierre);
        Assert.Equal(fechaOriginal, persistida.FechaCierre);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Cerrar_MotivoVacioONulo_Retorna400ProblemDetails(string? motivoInvalido)
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = motivoInvalido });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.BadRequest, cid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Abierta, persistida.Estado);
        Assert.Null(persistida.MotivoCierre);
    }

    [Fact]
    public async Task Cerrar_MotivoMayorA500Caracteres_Retorna400ProblemDetails()
    {
        var licitacion = CrearLicitacion();
        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var client = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var resp = await client.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = new string('x', 501) });

        await AssertProblemDetailsWithCorrelationIdAsync(resp, HttpStatusCode.BadRequest, cid);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        var persistida = await db.Licitaciones.SingleAsync(l => l.LicitacionId == licitacion.LicitacionId);
        Assert.Equal(EstadoLicitacion.Abierta, persistida.Estado);
    }

    [Fact]
    public async Task Cerrar_EfectosColaterales_DesapareceDeListadoProveedor_RechazaNuevasCotizaciones_YMarcaLicitacionCerradaEnMisCotizaciones()
    {
        var articuloId = Guid.NewGuid();
        var licitacion = CrearLicitacion(EstadoLicitacion.Abierta, null, articuloId);
        var proveedorCotizoAntesId = Guid.NewGuid();
        var proveedorTardeId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db => db.Licitaciones.Add(licitacion));

        var clientProveedor1 = factory.CreateClient();
        clientProveedor1.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorCotizoAntesId));

        // Proveedor 1 cotiza mientras la licitación está abierta
        var cotizarAntesResp = await clientProveedor1.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorCotizoAntesId,
            Flete = 500m,
            PlazoEntregaDias = 3,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articuloId, PrecioUnitario = 1200m }
            }
        });
        Assert.Equal(HttpStatusCode.Created, cotizarAntesResp.StatusCode);

        // El Comprador cierra la licitación sin adjudicar
        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        var cerrarResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licitacion.LicitacionId}/cierres",
            new { motivo = "La obra suspendió la compra de este ítem" });
        Assert.Equal(HttpStatusCode.OK, cerrarResp.StatusCode);

        // 1. Proveedor ya no ve la licitación en GET /api/licitaciones ni en GET /api/licitaciones/{id}
        var clientProveedor2 = factory.CreateClient();
        clientProveedor2.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Proveedor", proveedorTardeId));

        var listaProveedor = await clientProveedor2.GetFromJsonAsync<List<LicitacionResponseDto>>("/api/licitaciones");
        Assert.NotNull(listaProveedor);
        Assert.DoesNotContain(listaProveedor, l => l.LicitacionId == licitacion.LicitacionId);

        var detalleProveedorResp = await clientProveedor2.GetAsync($"/api/licitaciones/{licitacion.LicitacionId}");
        Assert.Equal(HttpStatusCode.NotFound, detalleProveedorResp.StatusCode);

        // Comprador sí la sigue viendo como Cerrada con motivo y fechaCierre
        var detalleComprador = await clientComprador.GetFromJsonAsync<LicitacionResponseDto>(
            $"/api/licitaciones/{licitacion.LicitacionId}");
        Assert.NotNull(detalleComprador);
        Assert.Equal("Cerrada", detalleComprador.Estado);
        Assert.Equal("La obra suspendió la compra de este ítem", detalleComprador.MotivoCierre);
        Assert.NotNull(detalleComprador.FechaCierre);

        // 2. Proveedor que intenta cotizar por POST /api/cotizaciones es rechazado
        var cid = Guid.NewGuid().ToString();
        clientProveedor2.DefaultRequestHeaders.Add("X-Correlation-ID", cid);
        var cotizarDespuesResp = await clientProveedor2.PostAsJsonAsync("/api/cotizaciones", new CrearCotizacionRequestDto
        {
            LicitacionId = licitacion.LicitacionId,
            ProveedorId = proveedorTardeId,
            Flete = 100m,
            PlazoEntregaDias = 2,
            Items = new List<CrearCotizacionItemDto>
            {
                new() { ArticuloId = articuloId, PrecioUnitario = 1000m }
            }
        });
        await AssertProblemDetailsWithCorrelationIdAsync(cotizarDespuesResp, HttpStatusCode.BadRequest, cid);

        // 3. Proveedor 1 ve "LicitacionCerrada" en GET /api/cotizaciones/mias
        var misCotizaciones = await clientProveedor1.GetFromJsonAsync<List<MiCotizacionResponseDto>>("/api/cotizaciones/mias");
        Assert.NotNull(misCotizaciones);
        var cotizacionMia = Assert.Single(misCotizaciones);
        Assert.Equal("Cerrada", cotizacionMia.EstadoLicitacion);
        Assert.False(cotizacionMia.EsGanadora);
        Assert.Equal("LicitacionCerrada", cotizacionMia.EstadoResultado);
    }

    [Fact]
    public async Task Cerrar_InvalidaMagicLinksPendientesYConservaLinksDeOtrasLicitaciones()
    {
        var articulo1 = Guid.NewGuid();
        var articulo2 = Guid.NewGuid();
        var licitacionCerrable = CrearLicitacion(EstadoLicitacion.Abierta, null, articulo1);
        var otraLicitacion = CrearLicitacion(EstadoLicitacion.Abierta, null, articulo2);
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(licitacionCerrable);
            db.Licitaciones.Add(otraLicitacion);
        });

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // Generar Magic Link pendiente para licitacionCerrable y otro para otraLicitacion
        var gen1Resp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionCerrable.LicitacionId}/magic-links",
            new { proveedorId });
        Assert.Equal(HttpStatusCode.OK, gen1Resp.StatusCode);
        using var gen1Doc = JsonDocument.Parse(await gen1Resp.Content.ReadAsStringAsync());
        var tokenLicitacionCerrable = gen1Doc.RootElement.GetProperty("token").GetString()!;

        var gen2Resp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{otraLicitacion.LicitacionId}/magic-links",
            new { proveedorId });
        Assert.Equal(HttpStatusCode.OK, gen2Resp.StatusCode);
        using var gen2Doc = JsonDocument.Parse(await gen2Resp.Content.ReadAsStringAsync());
        var tokenOtraLicitacion = gen2Doc.RootElement.GetProperty("token").GetString()!;

        // Cerrar la primera licitación
        var cerrarResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionCerrable.LicitacionId}/cierres",
            new { motivo = "Cancelada por cambio de proyecto" });
        Assert.Equal(HttpStatusCode.OK, cerrarResp.StatusCode);

        // Verificar en DB que el Magic Link pendiente de la licitación cerrada quedó invalidado
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var linkCerrado = await db.MagicLinks.SingleAsync(
                m => m.LicitacionId == licitacionCerrable.LicitacionId);
            var linkOtro = await db.MagicLinks.SingleAsync(
                m => m.LicitacionId == otraLicitacion.LicitacionId);

            Assert.True(linkCerrado.FechaDeVencimiento <= DateTime.UtcNow);
            Assert.Null(linkCerrado.FechaDeUso);
            Assert.True(linkOtro.FechaDeVencimiento > DateTime.UtcNow.AddMinutes(5));
        }

        // Intentar cotizar con el Magic Link de la licitación cerrada -> rechazado con 400
        var clientAnonimo = factory.CreateClient();
        var cid = Guid.NewGuid().ToString();
        clientAnonimo.DefaultRequestHeaders.Add("X-Correlation-ID", cid);

        var cotizarCerradaResp = await clientAnonimo.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{tokenLicitacionCerrable}/cotizaciones",
            new
            {
                flete = 100m,
                plazoEntregaDias = 2,
                items = new[]
                {
                    new { articuloId = articulo1, precioUnitario = 500m }
                }
            });
        await AssertProblemDetailsWithCorrelationIdAsync(cotizarCerradaResp, HttpStatusCode.BadRequest, cid);

        // El Magic Link de la otra licitación abierta sigue funcionando normalmente
        var cotizarOtraResp = await clientAnonimo.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{tokenOtraLicitacion}/cotizaciones",
            new
            {
                flete = 100m,
                plazoEntregaDias = 2,
                items = new[]
                {
                    new { articuloId = articulo2, precioUnitario = 500m }
                }
            });
        Assert.Equal(HttpStatusCode.OK, cotizarOtraResp.StatusCode);
    }
}
