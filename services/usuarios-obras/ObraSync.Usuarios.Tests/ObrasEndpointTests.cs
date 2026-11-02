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
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests;

public class ObrasEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosYObrasMinimo32Bytes!";
    private readonly WebApplicationFactory<Program> _factory;

    public ObrasEndpointTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"DB_Obras_Endpoint_{Guid.NewGuid()}";

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
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Fact]
    public async Task GetObras_SinToken_DebeRetornar401Unauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetObras_ConRolNoAutorizado_Proveedor_DebeRetornar403Forbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Proveedor));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Comprador)]
    [InlineData(UserRoles.Admin)]
    public async Task GetObras_ConRolesAutorizados_DebeRetornar200ConObrasEtapasYCapatazId(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(role));

        var response = await client.GetAsync("/api/Obras");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var obras = await response.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);
        Assert.True(obras.Count >= 3, "Deben existir al menos las 3 obras sembradas.");

        foreach (var obra in obras)
        {
            Assert.NotEqual(Guid.Empty, obra.Id);
            Assert.False(string.IsNullOrWhiteSpace(obra.Nombre));
            Assert.False(string.IsNullOrWhiteSpace(obra.Ubicacion));
            Assert.NotEqual(default, obra.FechaInicio);

            // Requisito #61: Debe incluir CapatazId y Capataz
            Assert.NotEqual(Guid.Empty, obra.CapatazId);
            Assert.Equal("Carlos Capataz", obra.Capataz);

            // Requisito #61: Cada obra debe incluir sus etapas constructivas
            Assert.NotNull(obra.Etapas);
            Assert.True(obra.Etapas.Count >= 3, $"La obra '{obra.Nombre}' debe contener al menos 3 etapas.");

            foreach (var etapa in obra.Etapas)
            {
                Assert.NotEqual(Guid.Empty, etapa.Id);
                Assert.False(string.IsNullOrWhiteSpace(etapa.NombreEtapa));
                Assert.Equal("Pendiente", etapa.Estado);
            }
        }
    }

    [Fact]
    public async Task PostObra_ConAdmin_DebeResponder201CreatedYReflejarseEnGetObrasConEtapasYCapatazId()
    {
        var client = _factory.CreateClient();

        // 1. Obtener capataz sembrado
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var obrasInicialesResp = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, obrasInicialesResp.StatusCode);
        var obrasIniciales = await obrasInicialesResp.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obrasIniciales);
        var capatazId = obrasIniciales.First().CapatazId;

        // 2. Crear nueva obra vía POST /api/Obras
        var nuevoNombre = $"Obra Test {Guid.NewGuid():N}"[..18];
        var crearDto = new CrearObraDto
        {
            Nombre = nuevoNombre,
            Ubicacion = "Av. Santa Fe 1234, CABA",
            FechaInicio = DateTime.UtcNow,
            CapatazId = capatazId,
            FasesEtapas = new List<string> { "Excavación", "Fundaciones", "Estructura", "Cubiertas" }
        };

        var postResponse = await client.PostAsJsonAsync("/api/Obras", crearDto);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var createdResult = await postResponse.Content.ReadFromJsonAsync<CrearObraResponseDto>();
        Assert.NotNull(createdResult);
        Assert.NotEqual(Guid.Empty, createdResult.Id);
        Assert.Equal("Obra creada exitosamente", createdResult.Mensaje);

        // 3. Consultar vía GET /api/Obras como Capataz y verificar que contenga la nueva obra con sus etapas y capatazId
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Capataz));

        var getResponse = await client.GetAsync("/api/Obras");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var obras = await getResponse.Content.ReadFromJsonAsync<List<ObraResponseDto>>();
        Assert.NotNull(obras);

        var obraCreada = obras.FirstOrDefault(o => o.Id == createdResult.Id);
        Assert.NotNull(obraCreada);
        Assert.Equal(nuevoNombre, obraCreada.Nombre);
        Assert.Equal(capatazId, obraCreada.CapatazId);
        Assert.Equal("Carlos Capataz", obraCreada.Capataz);
        Assert.Equal(4, obraCreada.Etapas.Count);
        Assert.Equal(new[] { "Excavación", "Fundaciones", "Estructura", "Cubiertas" },
            obraCreada.Etapas.Select(e => e.NombreEtapa).ToArray());
    }
}
