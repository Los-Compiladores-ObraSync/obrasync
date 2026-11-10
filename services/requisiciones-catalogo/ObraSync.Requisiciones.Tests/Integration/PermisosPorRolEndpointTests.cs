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
using ObraSync.Requisiciones.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class PermisosPorRolEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private static readonly Guid CementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");
        private static readonly Guid CalSeedId = Guid.Parse("11111111-1111-1111-1111-111111110003");

        private readonly WebApplicationFactory<Program> _factory;

        public PermisosPorRolEndpointTests(WebApplicationFactory<Program> factory)
        {
            var dbName = $"DB_Req_Permisos_{Guid.NewGuid()}";

            _factory = factory.WithWebHostBuilder(builder =>
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

                    // EF Core 9 guarda aparte la configuracion de AddDbContext (UseSqlServer). Si no se
                    // quita tambien, quedan registrados SqlServer e InMemory a la vez y el host no arranca.
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

        private static async Task AssertProblemDetailsWithCorrelationIdAsync(
            HttpResponseMessage response,
            HttpStatusCode expectedStatus,
            string expectedCorrelationId)
        {
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var body = await response.Content.ReadAsStringAsync();
            Assert.False(string.IsNullOrWhiteSpace(body));

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
            Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());
        }

        private async Task<Guid> CrearRequisicionSemillaAsync()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", Guid.NewGuid()));

            var response = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Observaciones = "Requisición semilla para pruebas de permisos",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 20m }
                }
            });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var dto = await response.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(dto);
            return dto.RequisicionId;
        }

        [Fact]
        public async Task TodosLosEndpoints_SinToken_DebenRetornar401UnauthorizedConProblemDetailsYCorrelationId()
        {
            var requisicionId = Guid.NewGuid();
            var correlationId = Guid.NewGuid().ToString();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

            // 1. GET /api/catalogo/insumos
            var getInsumosResp = await client.GetAsync("/api/catalogo/insumos");
            await AssertProblemDetailsWithCorrelationIdAsync(getInsumosResp, HttpStatusCode.Unauthorized, correlationId);

            // 2. POST /api/catalogo/insumos
            var postInsumoResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
            {
                Codigo = $"MAT-UNAUTH-{Guid.NewGuid():N}"[..16],
                Descripcion = "Insumo sin token",
                UnidadMedida = UnidadesMedida.Unidad
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postInsumoResp, HttpStatusCode.Unauthorized, correlationId);

            // 3. GET /api/requisiciones
            var getRequisicionesResp = await client.GetAsync("/api/requisiciones");
            await AssertProblemDetailsWithCorrelationIdAsync(getRequisicionesResp, HttpStatusCode.Unauthorized, correlationId);

            // 4. GET /api/requisiciones/{id}
            var getRequisicionByIdResp = await client.GetAsync($"/api/requisiciones/{requisicionId}");
            await AssertProblemDetailsWithCorrelationIdAsync(getRequisicionByIdResp, HttpStatusCode.Unauthorized, correlationId);

            // 5. POST /api/requisiciones
            var postRequisicionResp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 10m }
                }
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postRequisicionResp, HttpStatusCode.Unauthorized, correlationId);

            // 6. POST /api/requisiciones/{id}/aprobar
            var aprobarResp = await client.PostAsync($"/api/requisiciones/{requisicionId}/aprobar", null);
            await AssertProblemDetailsWithCorrelationIdAsync(aprobarResp, HttpStatusCode.Unauthorized, correlationId);

            // 7. POST /api/requisiciones/{id}/rechazar
            var rechazarResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{requisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Sin token" });
            await AssertProblemDetailsWithCorrelationIdAsync(rechazarResp, HttpStatusCode.Unauthorized, correlationId);
        }

        [Fact]
        public async Task TodosLosEndpoints_ConRolProveedor_CU01_DebenRetornar403ForbiddenConProblemDetailsYCorrelationId()
        {
            var requisicionExistenteId = await CrearRequisicionSemillaAsync();
            var correlationId = Guid.NewGuid().ToString();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Proveedor"));

            // 1. GET /api/catalogo/insumos
            var getInsumosResp = await client.GetAsync("/api/catalogo/insumos");
            await AssertProblemDetailsWithCorrelationIdAsync(getInsumosResp, HttpStatusCode.Forbidden, correlationId);

            // 2. POST /api/catalogo/insumos
            var postInsumoResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
            {
                Codigo = $"MAT-PROV-{Guid.NewGuid():N}"[..16],
                Descripcion = "Insumo proveedor no autorizado",
                UnidadMedida = UnidadesMedida.Unidad
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postInsumoResp, HttpStatusCode.Forbidden, correlationId);

            // 3. GET /api/requisiciones
            var getRequisicionesResp = await client.GetAsync("/api/requisiciones");
            await AssertProblemDetailsWithCorrelationIdAsync(getRequisicionesResp, HttpStatusCode.Forbidden, correlationId);

            // 4. GET /api/requisiciones/{id}
            var getRequisicionByIdResp = await client.GetAsync($"/api/requisiciones/{requisicionExistenteId}");
            await AssertProblemDetailsWithCorrelationIdAsync(getRequisicionByIdResp, HttpStatusCode.Forbidden, correlationId);

            // 5. POST /api/requisiciones
            var postRequisicionResp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 10m }
                }
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postRequisicionResp, HttpStatusCode.Forbidden, correlationId);

            // 6. POST /api/requisiciones/{id}/aprobar
            var aprobarResp = await client.PostAsync($"/api/requisiciones/{requisicionExistenteId}/aprobar", null);
            await AssertProblemDetailsWithCorrelationIdAsync(aprobarResp, HttpStatusCode.Forbidden, correlationId);

            // 7. POST /api/requisiciones/{id}/rechazar
            var rechazarResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{requisicionExistenteId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Intento de proveedor" });
            await AssertProblemDetailsWithCorrelationIdAsync(rechazarResp, HttpStatusCode.Forbidden, correlationId);
        }

        [Fact]
        public async Task RolCapataz_PermitidoEnLecturaYCreacionDeRequisicion_DenegadoEnAltaInsumoAprobarYRechazar()
        {
            var correlationId = Guid.NewGuid().ToString();
            var capatazId = Guid.NewGuid();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            // Permitido: GET /api/catalogo/insumos -> 200 OK
            var getInsumosResp = await client.GetAsync("/api/catalogo/insumos");
            Assert.Equal(HttpStatusCode.OK, getInsumosResp.StatusCode);

            // Permitido: POST /api/requisiciones -> 201 Created
            var postRequisicionResp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Fundaciones",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 35m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, postRequisicionResp.StatusCode);
            var creada = await postRequisicionResp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(creada);

            // Permitido: GET /api/requisiciones -> 200 OK
            var getRequisicionesResp = await client.GetAsync("/api/requisiciones");
            Assert.Equal(HttpStatusCode.OK, getRequisicionesResp.StatusCode);

            // Permitido: GET /api/requisiciones/{id} -> 200 OK
            var getByIdResp = await client.GetAsync($"/api/requisiciones/{creada.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getByIdResp.StatusCode);

            // Denegado: POST /api/catalogo/insumos -> 403 Forbidden (ProblemDetails + correlationId)
            var postInsumoResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
            {
                Codigo = $"MAT-CAP-{Guid.NewGuid():N}"[..16],
                Descripcion = "Intento de alta por Capataz",
                UnidadMedida = UnidadesMedida.Bolsa
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postInsumoResp, HttpStatusCode.Forbidden, correlationId);

            // Denegado: POST /api/requisiciones/{id}/aprobar -> 403 Forbidden (ProblemDetails + correlationId)
            var aprobarResp = await client.PostAsync($"/api/requisiciones/{creada.RequisicionId}/aprobar", null);
            await AssertProblemDetailsWithCorrelationIdAsync(aprobarResp, HttpStatusCode.Forbidden, correlationId);

            // Denegado: POST /api/requisiciones/{id}/rechazar -> 403 Forbidden (ProblemDetails + correlationId)
            var rechazarResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{creada.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Intento de rechazo por Capataz" });
            await AssertProblemDetailsWithCorrelationIdAsync(rechazarResp, HttpStatusCode.Forbidden, correlationId);
        }

        [Fact]
        public async Task RolComprador_PermitidoEnCatalogoLecturaYAprobacionRechazo_DenegadoEnCrearRequisicion()
        {
            var requisicionParaAprobarId = await CrearRequisicionSemillaAsync();
            var requisicionParaRechazarId = await CrearRequisicionSemillaAsync();
            var correlationId = Guid.NewGuid().ToString();
            var compradorId = Guid.NewGuid();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            // Permitido: GET /api/catalogo/insumos -> 200 OK
            var getInsumosResp = await client.GetAsync("/api/catalogo/insumos");
            Assert.Equal(HttpStatusCode.OK, getInsumosResp.StatusCode);

            // Permitido: POST /api/catalogo/insumos -> 201 Created
            var postInsumoResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
            {
                Codigo = $"MAT-COMP-{Guid.NewGuid():N}"[..16],
                Descripcion = "Insumo creado por Comprador",
                UnidadMedida = UnidadesMedida.Unidad,
                Moneda = Monedas.PesoArgentino,
                PrecioReferencia = 1200m,
                Activo = true
            });
            Assert.Equal(HttpStatusCode.Created, postInsumoResp.StatusCode);

            // Permitido: GET /api/requisiciones -> 200 OK
            var getRequisicionesResp = await client.GetAsync("/api/requisiciones");
            Assert.Equal(HttpStatusCode.OK, getRequisicionesResp.StatusCode);

            // Permitido: GET /api/requisiciones/{id} -> 200 OK
            var getByIdResp = await client.GetAsync($"/api/requisiciones/{requisicionParaAprobarId}");
            Assert.Equal(HttpStatusCode.OK, getByIdResp.StatusCode);

            // Permitido: POST /api/requisiciones/{id}/aprobar -> 200 OK
            var aprobarResp = await client.PostAsync($"/api/requisiciones/{requisicionParaAprobarId}/aprobar", null);
            Assert.Equal(HttpStatusCode.OK, aprobarResp.StatusCode);

            // Permitido: POST /api/requisiciones/{id}/rechazar -> 200 OK
            var rechazarResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{requisicionParaRechazarId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Rechazado por Comprador" });
            Assert.Equal(HttpStatusCode.OK, rechazarResp.StatusCode);

            // Denegado: POST /api/requisiciones -> 403 Forbidden (ProblemDetails + correlationId)
            var postRequisicionResp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CalSeedId, Cantidad = 15m }
                }
            });
            await AssertProblemDetailsWithCorrelationIdAsync(postRequisicionResp, HttpStatusCode.Forbidden, correlationId);
        }

        [Fact]
        public async Task RolAdmin_PermitidoEnTodosLosEndpoints()
        {
            var adminId = Guid.NewGuid();
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", Guid.NewGuid().ToString());
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Admin", adminId));

            // 1. GET /api/catalogo/insumos -> 200 OK
            var getInsumosResp = await client.GetAsync("/api/catalogo/insumos");
            Assert.Equal(HttpStatusCode.OK, getInsumosResp.StatusCode);

            // 2. POST /api/catalogo/insumos -> 201 Created
            var postInsumoResp = await client.PostAsJsonAsync("/api/catalogo/insumos", new CrearInsumoRequestDto
            {
                Codigo = $"MAT-ADM-{Guid.NewGuid():N}"[..16],
                Descripcion = "Insumo creado por Admin",
                UnidadMedida = UnidadesMedida.Bolsa,
                Moneda = Monedas.PesoArgentino,
                PrecioReferencia = 9500m,
                Activo = true
            });
            Assert.Equal(HttpStatusCode.Created, postInsumoResp.StatusCode);

            // 3. POST /api/requisiciones (crea dos requisiciones: una para aprobar y otra para rechazar) -> 201 Created
            var postReq1Resp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Estructura",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CementoSeedId, Cantidad = 40m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, postReq1Resp.StatusCode);
            var req1 = await postReq1Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req1);

            var postReq2Resp = await client.PostAsJsonAsync("/api/requisiciones", new CrearRequisicionRequestDto
            {
                ObraId = Guid.NewGuid(),
                Etapa = "Terminaciones",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = CalSeedId, Cantidad = 25m }
                }
            });
            Assert.Equal(HttpStatusCode.Created, postReq2Resp.StatusCode);
            var req2 = await postReq2Resp.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(req2);

            // 4. GET /api/requisiciones -> 200 OK
            var getRequisicionesResp = await client.GetAsync("/api/requisiciones");
            Assert.Equal(HttpStatusCode.OK, getRequisicionesResp.StatusCode);

            // 5. GET /api/requisiciones/{id} -> 200 OK
            var getByIdResp = await client.GetAsync($"/api/requisiciones/{req1.RequisicionId}");
            Assert.Equal(HttpStatusCode.OK, getByIdResp.StatusCode);

            // 6. POST /api/requisiciones/{id}/aprobar -> 200 OK
            var aprobarResp = await client.PostAsync($"/api/requisiciones/{req1.RequisicionId}/aprobar", null);
            Assert.Equal(HttpStatusCode.OK, aprobarResp.StatusCode);

            // 7. POST /api/requisiciones/{id}/rechazar -> 200 OK
            var rechazarResp = await client.PostAsJsonAsync(
                $"/api/requisiciones/{req2.RequisicionId}/rechazar",
                new RechazarRequisicionRequestDto { Motivo = "Rechazada por Admin" });
            Assert.Equal(HttpStatusCode.OK, rechazarResp.StatusCode);
        }
    }
}
