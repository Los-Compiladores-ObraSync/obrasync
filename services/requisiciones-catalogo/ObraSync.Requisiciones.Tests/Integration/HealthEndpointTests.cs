using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace ObraSync.Requisiciones.Tests.Integration
{
    public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public HealthEndpointTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
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
    }
}
