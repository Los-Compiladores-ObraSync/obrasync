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
using Microsoft.Extensions.Configuration;
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
                });
            });
        }

        private static string GenerateJwtToken(string role)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("sub", Guid.NewGuid().ToString()),
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
    }
}
