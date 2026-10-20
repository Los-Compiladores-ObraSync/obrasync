using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
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
    public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestJwtSecret = "ClaveDePruebaExclusivaParaIntegracionConLongitudMinima32Bytes!";
        private readonly WebApplicationFactory<Program> _factory;

        public HealthEndpointTests(WebApplicationFactory<Program> factory)
        {
            var dbName = $"DB_Req_Test_{Guid.NewGuid()}";

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
                new Claim("email", "test@obrasync.com"),
                new Claim("name", "Usuario Test"),
                new Claim("role", role)
            };

            var token = new JwtSecurityToken(
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [Fact]
        public async Task GetHealth_DebeResponderOkConNombreDeServicioYBaseDeDatos()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("ok", root.GetProperty("status").GetString());
            Assert.Equal("requisiciones-catalogo", root.GetProperty("service").GetString());
            Assert.Equal("DB_Req", root.GetProperty("database").GetString());
        }

        [Fact]
        public async Task GetInsumos_SinToken_DebeRetornarUnauthorized()
        {
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/catalogo/insumos");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task PostInsumo_ConRolCompradorYDatosValidos_DebeRetornarCreatedConMonedaARS()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador"));

            var request = new CrearInsumoRequestDto
            {
                Codigo = "MAT-LAD-001",
                Descripcion = "Ladrillo Hueco 12x18x33",
                UnidadMedida = UnidadesMedida.Unidad,
                Moneda = Monedas.PesoArgentino,
                PrecioReferencia = 650.00m,
                Activo = true
            };

            var response = await client.PostAsJsonAsync("/api/catalogo/insumos", request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var creado = await response.Content.ReadFromJsonAsync<InsumoCatalogoDto>();
            Assert.NotNull(creado);
            Assert.NotEqual(Guid.Empty, creado.ArticuloId);
            Assert.Equal("MAT-LAD-001", creado.Codigo);
            Assert.Equal(Monedas.PesoArgentino, creado.Moneda);
            Assert.Equal(650.00m, creado.PrecioReferencia);
        }

        [Fact]
        public async Task PostInsumo_ConRolCapataz_DebeRetornarForbidden()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz"));

            var request = new CrearInsumoRequestDto
            {
                Codigo = "MAT-LAD-002",
                Descripcion = "Ladrillo Común",
                UnidadMedida = UnidadesMedida.Unidad
            };

            var response = await client.PostAsJsonAsync("/api/catalogo/insumos", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task FlujoPuntaAPunta_CU02_CapatazCreaRequisicionYCompradorAprueba_DebeRetornarAprobada()
        {
            var capatazId = Guid.NewGuid();
            var compradorId = Guid.NewGuid();
            var obraId = Guid.NewGuid();
            var cementoSeedId = Guid.Parse("11111111-1111-1111-1111-111111110001");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", Guid.NewGuid().ToString());

            // 1. Capataz crea requisición de 80 bolsas de cemento para etapa Estructura
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Capataz", capatazId));

            var crearRequest = new CrearRequisicionRequestDto
            {
                ObraId = obraId,
                Etapa = "Estructura",
                Observaciones = "Hormigonado losa piso 2",
                Items = new List<CrearRequisicionItemDto>
                {
                    new() { ArticuloId = cementoSeedId, Cantidad = 80m }
                }
            };

            var createResponse = await client.PostAsJsonAsync("/api/requisiciones", crearRequest);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

            var requisicionCreada = await createResponse.Content.ReadFromJsonAsync<RequisicionResponseDto>();
            Assert.NotNull(requisicionCreada);
            Assert.NotEqual(Guid.Empty, requisicionCreada.RequisicionId);
            Assert.Equal(obraId, requisicionCreada.ObraId);
            Assert.Equal("Estructura", requisicionCreada.Etapa);
            Assert.Equal(capatazId, requisicionCreada.SolicitanteId);
            Assert.Equal(EstadoRequisicion.Pendiente, requisicionCreada.Estado);
            Assert.Null(requisicionCreada.AprobadorId);
            Assert.Single(requisicionCreada.Items);
            Assert.Equal(80m, requisicionCreada.Items[0].Cantidad);
            Assert.Equal(UnidadesMedida.Bolsa, requisicionCreada.Items[0].UnidadMedida);

            // 2. Comprador lista las requisiciones pendientes de esa obra
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", GenerateJwtToken("Comprador", compradorId));

            var listResponse = await client.GetAsync($"/api/requisiciones?obraId={obraId}&estado=Pendiente");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

            var lista = await listResponse.Content.ReadFromJsonAsync<List<RequisicionResponseDto>>();
            Assert.NotNull(lista);
            Assert.Single(lista);

            // 3. Comprador aprueba la requisición (cambia a Aprobada y publica RequisicionAprobadaEvent)
            var aprobarResponse = await client.PostAsync(
                $"/api/requisiciones/{requisicionCreada.RequisicionId}/aprobar",
                content: null);

            Assert.Equal(HttpStatusCode.OK, aprobarResponse.StatusCode);

            using var aprobarJson = JsonDocument.Parse(await aprobarResponse.Content.ReadAsStringAsync());
            Assert.EndsWith("Z", aprobarJson.RootElement.GetProperty("fechaCreacion").GetString());
            Assert.EndsWith("Z", aprobarJson.RootElement.GetProperty("fechaAprobacion").GetString());

            var requisicionAprobada = aprobarJson.RootElement.Deserialize<RequisicionResponseDto>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(requisicionAprobada);
            Assert.Equal(EstadoRequisicion.Aprobada, requisicionAprobada.Estado);
            Assert.Equal(compradorId, requisicionAprobada.AprobadorId);
            Assert.NotNull(requisicionAprobada.FechaAprobacion);

            // 4. Si se intenta aprobar nuevamente, retorna 409 Conflict con ProblemDetails
            var segundoIntento = await client.PostAsync(
                $"/api/requisiciones/{requisicionCreada.RequisicionId}/aprobar",
                content: null);

            Assert.Equal(HttpStatusCode.Conflict, segundoIntento.StatusCode);
        }
    }
}
