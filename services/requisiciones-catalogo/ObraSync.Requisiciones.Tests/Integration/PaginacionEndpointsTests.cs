using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class PaginacionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private static readonly Guid CementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");

        private readonly WebApplicationFactory<Program> _baseFactory;

        public PaginacionEndpointsTests(WebApplicationFactory<Program> factory)
        {
            _baseFactory = factory;
        }

        private WebApplicationFactory<Program> CreateIsolatedFactory(Action<RequisicionesDbContext>? seedAction = null)
        {
            var dbName = $"DB_Req_Paginacion_{Guid.NewGuid()}";

            return _baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Jwt:Secret", TestJwtSecret);
                builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");
                builder.UseSetting("RabbitMQ:UseInMemory", "true");

                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<RequisicionesDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    var configuraciones = services.Where(d =>
                            d.ServiceType.IsGenericType &&
                            d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration") &&
                            d.ServiceType.GenericTypeArguments[0] == typeof(RequisicionesDbContext))
                        .ToList();
                    foreach (var configuracion in configuraciones)
                    {
                        services.Remove(configuracion);
                    }

                    services.AddDbContext<RequisicionesDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });

                    using var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RequisicionesDbContext>();
                    db.Database.EnsureCreated();
                    seedAction?.Invoke(db);
                });
            });
        }

        private static string GenerateJwtToken(string role, Guid? userId = null)
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

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal(400, root.GetProperty("status").GetInt32());
            Assert.Equal("Paginación inválida", root.GetProperty("title").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
            Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        }

        [Fact]
        public async Task GetRequisiciones_PrimeraYUltimaPagina_DevuelveItemsYCabecerasDePaginacion()
        {
            var obraId = Guid.NewGuid();
            var solicitanteId = Guid.NewGuid();
            var fechaBase = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

            using var factory = CreateIsolatedFactory(db =>
            {
                for (int i = 1; i <= 5; i++)
                {
                    var reqId = Guid.NewGuid();
                    var req = new Requisicion(
                        reqId,
                        $"REQ-2026-000{i}",
                        obraId,
                        solicitanteId,
                        new[]
                        {
                            new RequisicionItem(Guid.NewGuid(), reqId, CementoSeedId, "Cemento Portland", 10m * i, UnidadesMedida.Bolsa)
                        },
                        etapa: "Estructura",
                        fechaCreacion: fechaBase.AddMinutes(i));
                    db.Requisiciones.Add(req);
                }
                db.SaveChanges();
            });

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            // Primera página: pagina=1&tamano=2 -> 2 ítems (REQ-2026-0005 y REQ-2026-0004), X-Total-Count=5
            var resp1 = await client.GetAsync("/api/requisiciones?pagina=1&tamano=2");
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            Assert.Equal("5", resp1.Headers.GetValues("X-Total-Count").Single());
            Assert.Equal("1", resp1.Headers.GetValues("X-Page").Single());
            Assert.Equal("2", resp1.Headers.GetValues("X-Page-Size").Single());

            var items1 = await resp1.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(items1);
            Assert.Equal(2, items1.Count);
            Assert.Equal("REQ-2026-0005", items1[0].NumeroRequisicion);
            Assert.Equal("REQ-2026-0004", items1[1].NumeroRequisicion);

            // Última página: pagina=3&tamano=2 -> 1 ítem (REQ-2026-0001), X-Total-Count=5
            var resp3 = await client.GetAsync("/api/requisiciones?pagina=3&tamano=2");
            Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
            Assert.Equal("5", resp3.Headers.GetValues("X-Total-Count").Single());
            Assert.Equal("3", resp3.Headers.GetValues("X-Page").Single());
            Assert.Equal("2", resp3.Headers.GetValues("X-Page-Size").Single());

            var items3 = await resp3.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(items3);
            Assert.Single(items3);
            Assert.Equal("REQ-2026-0001", items3[0].NumeroRequisicion);
        }

        [Fact]
        public async Task GetRequisiciones_ConMismaFechaCreacion_DesempataPorIdSinDuplicarNiPerderFilasEntrePaginas()
        {
            var obraId = Guid.NewGuid();
            var solicitanteId = Guid.NewGuid();
            var fechaIdentica = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
            var idsEsperados = new[]
            {
                Guid.Parse("00000000-0000-0000-0000-000000000010"),
                Guid.Parse("00000000-0000-0000-0000-000000000020"),
                Guid.Parse("00000000-0000-0000-0000-000000000030"),
                Guid.Parse("00000000-0000-0000-0000-000000000040")
            };

            using var factory = CreateIsolatedFactory(db =>
            {
                // Insertamos en orden desordenado de Id pero con idéntica FechaCreacion
                foreach (var id in new[] { idsEsperados[2], idsEsperados[0], idsEsperados[3], idsEsperados[1] })
                {
                    var req = new Requisicion(
                        id,
                        $"REQ-{id.ToString()[..8]}",
                        obraId,
                        solicitanteId,
                        new[]
                        {
                            new RequisicionItem(Guid.NewGuid(), id, CementoSeedId, "Cemento Portland", 15m, UnidadesMedida.Bolsa)
                        },
                        etapa: "Fundaciones",
                        fechaCreacion: fechaIdentica);
                    db.Requisiciones.Add(req);
                }
                db.SaveChanges();
            });

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var respP1 = await client.GetAsync("/api/requisiciones?pagina=1&tamano=2");
            var respP2 = await client.GetAsync("/api/requisiciones?pagina=2&tamano=2");

            var p1 = await respP1.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            var p2 = await respP2.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();

            Assert.NotNull(p1);
            Assert.NotNull(p2);
            Assert.Equal(2, p1.Count);
            Assert.Equal(2, p2.Count);

            var recibidos = p1.Concat(p2).Select(r => r.RequisicionId).ToList();
            Assert.Equal(idsEsperados, recibidos);
        }

        [Fact]
        public async Task GetRequisiciones_XTotalCount_RespetaFiltrosDeObraEstadoYSolicitante()
        {
            var obraA = Guid.NewGuid();
            var obraB = Guid.NewGuid();
            var capatazA = Guid.NewGuid();
            var capatazB = Guid.NewGuid();
            var aprobadorId = Guid.NewGuid();

            using var factory = CreateIsolatedFactory(db =>
            {
                // 3 en obraA de capatazA (2 Pendientes, 1 Aprobada) + 2 en obraB de capatazB (Pendientes)
                for (int i = 1; i <= 3; i++)
                {
                    var id = Guid.NewGuid();
                    var req = new Requisicion(
                        id,
                        $"REQ-A-{i}",
                        obraA,
                        capatazA,
                        new[] { new RequisicionItem(Guid.NewGuid(), id, CementoSeedId, "Cemento", 10m, UnidadesMedida.Bolsa) },
                        etapa: "Estructura");
                    if (i == 3)
                    {
                        req.Aprobar(aprobadorId, DateTime.UtcNow);
                    }
                    db.Requisiciones.Add(req);
                }

                for (int i = 1; i <= 2; i++)
                {
                    var id = Guid.NewGuid();
                    var req = new Requisicion(
                        id,
                        $"REQ-B-{i}",
                        obraB,
                        capatazB,
                        new[] { new RequisicionItem(Guid.NewGuid(), id, CementoSeedId, "Cemento", 10m, UnidadesMedida.Bolsa) },
                        etapa: "Estructura");
                    db.Requisiciones.Add(req);
                }

                db.SaveChanges();
            });

            var clientComprador = factory.CreateClient();
            clientComprador.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            // Filtro por obraA -> X-Total-Count = 3 (aunque pidamos tamano=1)
            var respObraA = await clientComprador.GetAsync($"/api/requisiciones?obraId={obraA}&pagina=1&tamano=1");
            Assert.Equal(HttpStatusCode.OK, respObraA.StatusCode);
            Assert.Equal("3", respObraA.Headers.GetValues("X-Total-Count").Single());

            // Filtro por obraA y estado=Pendiente -> X-Total-Count = 2
            var respObraAPendiente = await clientComprador.GetAsync($"/api/requisiciones?obraId={obraA}&estado=Pendiente&pagina=1&tamano=1");
            Assert.Equal(HttpStatusCode.OK, respObraAPendiente.StatusCode);
            Assert.Equal("2", respObraAPendiente.Headers.GetValues("X-Total-Count").Single());

            // Rol Capataz (capatazB) -> solo ve las suyas -> X-Total-Count = 2
            var clientCapatazB = factory.CreateClient();
            clientCapatazB.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazB));

            var respCapatazB = await clientCapatazB.GetAsync("/api/requisiciones?pagina=1&tamano=1");
            Assert.Equal(HttpStatusCode.OK, respCapatazB.StatusCode);
            Assert.Equal("2", respCapatazB.Headers.GetValues("X-Total-Count").Single());
        }

        [Theory]
        [InlineData("/api/requisiciones?tamano=51")]
        [InlineData("/api/requisiciones?pagina=0")]
        [InlineData("/api/requisiciones?tamano=0")]
        [InlineData("/api/catalogo/insumos?tamano=51")]
        [InlineData("/api/catalogo/insumos?pagina=0")]
        [InlineData("/api/catalogo/insumos?tamano=0")]
        public async Task EndpointsPaginados_ParametrosFueraDeRango_Retornan400ProblemDetailsConCorrelationId(string url)
        {
            using var factory = CreateIsolatedFactory();
            var correlationId = Guid.NewGuid().ToString();

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var response = await client.GetAsync(url);
            await AssertProblemDetails400Async(response, correlationId);
        }

        [Fact]
        public async Task GetInsumos_PaginacionYFiltros_DevuelveItemsYCabecerasExactas()
        {
            using var factory = CreateIsolatedFactory(db =>
            {
                // El seed inicial de RequisicionesDbContext ya crea 12 insumos activos; agregamos 1 inactivo
                db.Insumos.Add(new Insumo(
                    Guid.NewGuid(),
                    "MAT-ZZZ-999",
                    "Insumo Inactivo Especial Paginación",
                    UnidadesMedida.Unidad,
                    activo: false,
                    precioReferencia: 500m));
                db.SaveChanges();
            });

            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            // 1. Primera página de activos (11 totales en seed) con tamano=5 -> 5 ítems, X-Total-Count=11
            var respP1 = await client.GetAsync("/api/catalogo/insumos?pagina=1&tamano=5");
            Assert.Equal(HttpStatusCode.OK, respP1.StatusCode);
            Assert.Equal("11", respP1.Headers.GetValues("X-Total-Count").Single());
            Assert.Equal("1", respP1.Headers.GetValues("X-Page").Single());
            Assert.Equal("5", respP1.Headers.GetValues("X-Page-Size").Single());

            var itemsP1 = await respP1.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(itemsP1);
            Assert.Equal(5, itemsP1.Count);

            // 2. Última página de activos: pagina=3&tamano=5 -> 1 ítem, X-Total-Count=11
            var respP3 = await client.GetAsync("/api/catalogo/insumos?pagina=3&tamano=5");
            Assert.Equal(HttpStatusCode.OK, respP3.StatusCode);
            Assert.Equal("11", respP3.Headers.GetValues("X-Total-Count").Single());
            Assert.Equal("3", respP3.Headers.GetValues("X-Page").Single());
            Assert.Equal("5", respP3.Headers.GetValues("X-Page-Size").Single());

            var itemsP3 = await respP3.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(itemsP3);
            Assert.Single(itemsP3);

            // 3. Con soloActivos=false -> X-Total-Count=12 (11 activos del seed + 1 inactivo agregado)
            var respTodos = await client.GetAsync("/api/catalogo/insumos?soloActivos=false&pagina=1&tamano=5");
            Assert.Equal(HttpStatusCode.OK, respTodos.StatusCode);
            Assert.Equal("12", respTodos.Headers.GetValues("X-Total-Count").Single());

            // 4. Con filtro search="Cemento" -> X-Total-Count refleja solo los que coinciden con "Cemento"
            var respSearch = await client.GetAsync("/api/catalogo/insumos?search=Cemento&pagina=1&tamano=1");
            Assert.Equal(HttpStatusCode.OK, respSearch.StatusCode);
            Assert.Equal("2", respSearch.Headers.GetValues("X-Total-Count").Single());
            var itemsSearch = await respSearch.Content.ReadFromJsonAsync<List<InsumoCatalogoDto>>();
            Assert.NotNull(itemsSearch);
            Assert.Single(itemsSearch);
        }
    }
}
