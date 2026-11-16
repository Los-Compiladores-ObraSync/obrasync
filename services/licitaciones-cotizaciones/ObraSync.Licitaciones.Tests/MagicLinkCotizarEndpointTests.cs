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
using ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Infrastructure;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class MagicLinkCotizarEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "clave-de-pruebas-de-al-menos-32-caracteres";
    private readonly WebApplicationFactory<Program> _baseFactory;

    public MagicLinkCotizarEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);
        _baseFactory = factory;
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory(
        Action<LicitacionesDbContext>? seedAction = null,
        string? horasVigenciaConfig = null)
    {
        var dbName = $"DB_Licit_MagicLink_{Guid.NewGuid()}";

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
    public async Task CotizarConMagicLink_PrimerUsoOk_PersisteCotizacionConItemsYMarcaEnlaceComoUsadoConHashSha256()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliego1Id = Guid.NewGuid();
        var itemPliego2Id = Guid.NewGuid();
        var articulo1Id = Guid.NewGuid();
        var articulo2Id = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0800",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3),
                Items = new List<LicitacionItem>
                {
                    new()
                    {
                        Id = itemPliego1Id,
                        LicitacionId = licitacionId,
                        ArticuloId = articulo1Id,
                        Descripcion = "Cemento Portland",
                        Cantidad = 10m,
                        UnidadMedida = "BOLSA"
                    },
                    new()
                    {
                        Id = itemPliego2Id,
                        LicitacionId = licitacionId,
                        ArticuloId = articulo2Id,
                        Descripcion = "Cal Hidratada",
                        Cantidad = 5m,
                        UnidadMedida = "BOLSA"
                    }
                }
            });
        }, horasVigenciaConfig: "4");

        var clientComprador = factory.CreateClient();
        clientComprador.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerarTokenJwt("Comprador"));

        // 1. El comprador genera el Magic Link para el proveedor
        var genResp = await clientComprador.PostAsJsonAsync(
            $"/api/licitaciones/{licitacionId}/magic-links",
            new { proveedorId });
        Assert.Equal(HttpStatusCode.OK, genResp.StatusCode);

        using var genDoc = JsonDocument.Parse(await genResp.Content.ReadAsStringAsync());
        var rawToken = genDoc.RootElement.GetProperty("hash").GetString();
        Assert.False(string.IsNullOrWhiteSpace(rawToken));

        // Verificamos en DB que se guardó el hash SHA-256 del token y que la vigencia tomó las 4 horas configuradas
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var linkGuardado = await db.MagicLinks.SingleAsync();
            Assert.Equal(MagicLinkHashHelper.ComputeSha256(rawToken!), linkGuardado.Hash);
            Assert.Null(linkGuardado.FechaDeUso);
            Assert.True(linkGuardado.FechaDeVencimiento > DateTime.UtcNow.AddHours(3.5));
        }

        // 2. El proveedor cotiza sin token JWT usando el Magic Link (enviando LicitacionItemId y ArticuloId)
        var clientAnonimo = factory.CreateClient();
        var cotizarResp = await clientAnonimo.PostAsJsonAsync(
            $"/api/licitaciones/magic-links/{rawToken}/cotizar",
            new
            {
                flete = 250m,
                plazoEntregaDias = 4,
                items = new object[]
                {
                    new { licitacionItemId = itemPliego1Id, precioUnitario = 100m }, // 10 * 100 = 1000
                    new { licitacionItemId = itemPliego2Id, precioUnitario = 80m }   // 5 * 80 = 400
                }
            });

        Assert.Equal(HttpStatusCode.OK, cotizarResp.StatusCode);
        var dtoCreada = await cotizarResp.Content.ReadFromJsonAsync<CotizacionResponseDto>();
        Assert.NotNull(dtoCreada);
        Assert.Equal(licitacionId, dtoCreada.LicitacionId);
        Assert.Equal(proveedorId, dtoCreada.ProveedorId);
        Assert.Equal(2, dtoCreada.Items.Count);
        Assert.Equal(1400m, dtoCreada.SubtotalItems);
        Assert.Equal(250m, dtoCreada.Flete);
        Assert.Equal(1650m, dtoCreada.CostoTotal);

        // 3. Verificamos que la cotización y sus ítems quedaron realmente persistidos en la BD y visibles en GET /api/cotizaciones
        var listResp = await clientComprador.GetAsync($"/api/cotizaciones?licitacionId={licitacionId}");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var lista = await listResp.Content.ReadFromJsonAsync<List<CotizacionResponseDto>>();
        Assert.NotNull(lista);
        var persistida = Assert.Single(lista);
        Assert.Equal(2, persistida.Items.Count);
        Assert.Equal(1400m, persistida.SubtotalItems);
        Assert.Equal(1650m, persistida.CostoTotal);
        Assert.True(persistida.EsMasBarata);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
            var linkUsado = await db.MagicLinks.SingleAsync();
            Assert.NotNull(linkUsado.FechaDeUso);
        }
    }

    [Fact]
    public async Task CotizarConMagicLink_SegundoUsoRechazadoConProblemDetailsYCorrelationId()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var rawToken = "token-unico-segundo-uso";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacionUnItem(licitacionId, itemPliegoId, articuloId));
            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(rawToken),
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                FechaDeVencimiento = DateTime.UtcNow.AddHours(1),
                FechaDeUso = null
            });
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var payload = new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = new[] { new { licitacionItemId = itemPliegoId, precioUnitario = 50m } }
        };

        // 1. Primer uso: 200 OK
        var resp1 = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", payload);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // 2. Segundo uso con el mismo enlace: 409 Conflict con ProblemDetails y correlationId
        var resp2 = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", payload);
        await AssertProblemDetailsAsync(resp2, HttpStatusCode.Conflict, correlationId);

        // Solo existe 1 cotización en la base de datos
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Equal(1, await db.Cotizaciones.CountAsync());
    }

    [Fact]
    public async Task CotizarConMagicLink_EnlaceVencidoOInexistente_RechazaConProblemDetailsSinPersistirCotizacion()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var tokenVencido = "token-vencido-123";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacionUnItem(licitacionId, itemPliegoId, articuloId));
            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(tokenVencido),
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                FechaDeVencimiento = DateTime.UtcNow.AddMinutes(-5), // ya vencido
                FechaDeUso = null
            });
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var payload = new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = new[] { new { licitacionItemId = itemPliegoId, precioUnitario = 50m } }
        };

        // 1. Enlace vencido -> 400 Bad Request ProblemDetails
        var respVencido = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{tokenVencido}/cotizar", payload);
        await AssertProblemDetailsAsync(respVencido, HttpStatusCode.BadRequest, correlationId);

        // 2. Enlace inexistente -> 404 Not Found ProblemDetails
        var respInexistente = await client.PostAsJsonAsync("/api/licitaciones/magic-links/token-que-no-existe/cotizar", payload);
        await AssertProblemDetailsAsync(respInexistente, HttpStatusCode.NotFound, correlationId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Equal(0, await db.Cotizaciones.CountAsync());
    }

    [Fact]
    public async Task CotizarConMagicLink_ItemsInvalidos_RechazaCon400SinQuemarElEnlaceNiDistorsionarComparativa()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliego1Id = Guid.NewGuid();
        var itemPliego2Id = Guid.NewGuid();
        var articulo1Id = Guid.NewGuid();
        var articulo2Id = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var rawToken = "token-validacion-items";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(new Licitacion
            {
                LicitacionId = licitacionId,
                RequisicionId = Guid.NewGuid(),
                NumeroRequisicion = "REQ-2026-0810",
                ObraId = Guid.NewGuid(),
                Estado = EstadoLicitacion.Abierta,
                FechaCreacion = DateTime.UtcNow.AddHours(-1),
                FechaLimiteCotizacion = DateTime.UtcNow.AddDays(3),
                Items = new List<LicitacionItem>
                {
                    new() { Id = itemPliego1Id, LicitacionId = licitacionId, ArticuloId = articulo1Id, Descripcion = "Cemento", Cantidad = 10m, UnidadMedida = "BOLSA" },
                    new() { Id = itemPliego2Id, LicitacionId = licitacionId, ArticuloId = articulo2Id, Descripcion = "Arena", Cantidad = 5m, UnidadMedida = "M3" }
                }
            });

            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(rawToken),
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                FechaDeVencimiento = DateTime.UtcNow.AddHours(1),
                FechaDeUso = null
            });
        });

        var correlationId = Guid.NewGuid().ToString();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        // a) Lista de ítems vacía -> 400 Bad Request
        var respVacios = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = Array.Empty<object>()
        });
        await AssertProblemDetailsAsync(respVacios, HttpStatusCode.BadRequest, correlationId);

        // b) PrecioUnitario <= 0 -> 400 Bad Request
        var respPrecioCero = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = new[]
            {
                new { licitacionItemId = itemPliego1Id, precioUnitario = 0m },
                new { licitacionItemId = itemPliego2Id, precioUnitario = 100m }
            }
        });
        await AssertProblemDetailsAsync(respPrecioCero, HttpStatusCode.BadRequest, correlationId);

        // c) Falta un ítem del pliego -> 400 Bad Request
        var respIncompleto = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = new[]
            {
                new { licitacionItemId = itemPliego1Id, precioUnitario = 120m }
            }
        });
        await AssertProblemDetailsAsync(respIncompleto, HttpStatusCode.BadRequest, correlationId);

        // Verificamos que el Magic Link NO fue quemado y que el proveedor puede corregir su carga y enviarla con éxito
        var respValida = await client.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", new
        {
            flete = 100m,
            plazoEntregaDias = 3,
            items = new[]
            {
                new { licitacionItemId = itemPliego1Id, precioUnitario = 120m },
                new { licitacionItemId = itemPliego2Id, precioUnitario = 80m }
            }
        });
        Assert.Equal(HttpStatusCode.OK, respValida.StatusCode);
    }

    [Fact]
    public async Task CotizarConMagicLink_DosPedidosSimultaneosConMismoEnlace_SoloUnoGanaYElOtroRecibe409ProblemDetails()
    {
        var licitacionId = Guid.NewGuid();
        var itemPliegoId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();
        var rawToken = $"token-concurrente-{Guid.NewGuid():N}";

        using var factory = CreateIsolatedFactory(db =>
        {
            db.Licitaciones.Add(CrearLicitacionUnItem(licitacionId, itemPliegoId, articuloId));
            db.MagicLinks.Add(new MagicLink
            {
                Id = Guid.NewGuid(),
                Hash = MagicLinkHashHelper.ComputeSha256(rawToken),
                LicitacionId = licitacionId,
                ProveedorId = proveedorId,
                FechaDeVencimiento = DateTime.UtcNow.AddHours(1),
                FechaDeUso = null
            });
        });

        var client1 = factory.CreateClient();
        var client2 = factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString();
        client1.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client2.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var payload = new
        {
            flete = 150m,
            plazoEntregaDias = 4,
            items = new[] { new { licitacionItemId = itemPliegoId, precioUnitario = 95m } }
        };

        var respuestas = await Task.WhenAll(
            client1.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", payload),
            client2.PostAsJsonAsync($"/api/licitaciones/magic-links/{rawToken}/cotizar", payload));

        var exitosas = respuestas.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var rechazadas = respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict).ToList();

        Assert.Single(exitosas);
        var rechazada = Assert.Single(rechazadas);
        await AssertProblemDetailsAsync(rechazada, HttpStatusCode.Conflict, correlationId);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicitacionesDbContext>();
        Assert.Equal(1, await db.Cotizaciones.CountAsync());
        Assert.Equal(1, await db.CotizacionItems.CountAsync());
    }

    private static Licitacion CrearLicitacionUnItem(Guid licitacionId, Guid itemPliegoId, Guid articuloId)
    {
        return new Licitacion
        {
            LicitacionId = licitacionId,
            RequisicionId = Guid.NewGuid(),
            NumeroRequisicion = "REQ-2026-0801",
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
                    Descripcion = "Cemento Portland",
                    Cantidad = 10m,
                    UnidadMedida = "BOLSA"
                }
            }
        };
    }
}
