using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class PlantillasObraEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosYObrasMinimo32Bytes!";
    private readonly WebApplicationFactory<Program> _factory;

    public PlantillasObraEndpointTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"DB_Plantillas_Endpoint_{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestJwtSecret);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "InMemoryTestDb");

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AuthDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<AuthDbContext>(options =>
                {
                    options.UseInMemoryDatabase(dbName);
                });
            });
        });
    }

    private static string GenerateJwtToken(string role, Guid? userId = null, string email = "test@obrasync.com")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", (userId ?? Guid.NewGuid()).ToString()),
            new Claim("email", email),
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

    [Fact]
    public async Task GetPlantillas_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/plantillas-obra");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPlantillas_ConRolNoAutorizado_Proveedor_DebeRetornar403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Proveedor));

        var response = await client.GetAsync("/api/plantillas-obra");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Comprador)]
    [InlineData(UserRoles.Admin)]
    public async Task GetPlantillas_ConRolesAutorizados_DebeRetornar200YPlantillasSembradas(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var response = await client.GetAsync("/api/plantillas-obra");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var plantillas = await response.Content.ReadFromJsonAsync<List<PlantillaObraResponseDto>>();
        Assert.NotNull(plantillas);
        Assert.True(plantillas.Count >= 3, "Deben existir al menos las 3 plantillas sembradas.");

        foreach (var plantilla in plantillas)
        {
            Assert.NotEqual(Guid.Empty, plantilla.Id);
            Assert.False(string.IsNullOrWhiteSpace(plantilla.Nombre));
            Assert.NotNull(plantilla.Etapas);
            Assert.NotEmpty(plantilla.Etapas);

            int ordenEsperado = 1;
            foreach (var etapa in plantilla.Etapas)
            {
                Assert.NotEqual(Guid.Empty, etapa.Id);
                Assert.False(string.IsNullOrWhiteSpace(etapa.NombreEtapa));
                Assert.Equal(ordenEsperado++, etapa.Orden);
            }
        }
    }

    [Fact]
    public async Task PostPlantilla_ComoComprador_DebeCrear201YReflejarseEnGetPorId()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Comprador));

        var nuevaPlantillaDto = new CrearPlantillaObraRequestDto
        {
            Nombre = "Plantilla Dúplex Estándar",
            Descripcion = "Prototipo de obra para dúplex",
            Etapas = new List<string> { "Plano y Replanteo", "Cimientos", "Mampostería", "Techado" }
        };

        var postResponse = await client.PostAsJsonAsync("/api/plantillas-obra", nuevaPlantillaDto);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var creada = await postResponse.Content.ReadFromJsonAsync<PlantillaObraResponseDto>();
        Assert.NotNull(creada);
        Assert.NotEqual(Guid.Empty, creada.Id);
        Assert.Equal("Plantilla Dúplex Estándar", creada.Nombre);
        Assert.Equal(4, creada.Etapas.Count);

        // Consultar por ID con rol Capataz
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz));

        var getResponse = await client.GetAsync($"/api/plantillas-obra/{creada.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var plantillaRecuperada = await getResponse.Content.ReadFromJsonAsync<PlantillaObraResponseDto>();
        Assert.NotNull(plantillaRecuperada);
        Assert.Equal(creada.Id, plantillaRecuperada.Id);
        Assert.Equal(4, plantillaRecuperada.Etapas.Count);
        Assert.Equal("Plano y Replanteo", plantillaRecuperada.Etapas[0].NombreEtapa);
        Assert.Equal(1, plantillaRecuperada.Etapas[0].Orden);
    }

    [Fact]
    public async Task PostPlantilla_ComoCapataz_DebeRetornar403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz));

        var nuevaPlantillaDto = new CrearPlantillaObraRequestDto
        {
            Nombre = "Plantilla No Autorizada",
            Descripcion = "Capataz no tiene permisos para crear plantillas",
            Etapas = new List<string> { "Etapa 1" }
        };

        var postResponse = await client.PostAsJsonAsync("/api/plantillas-obra", nuevaPlantillaDto);

        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }
}