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
            builder.UseSetting("RabbitMQ:UseInMemory", "true");

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

    // ── Tarjeta #182: Paginación en GET /api/plantillas-obra ─────────────────

    [Fact]
    public async Task GetPlantillas_PrimeraYUltimaPagina_DevuelveCantidadCorrectaYHeaders()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        var respTotal = await client.GetAsync("/api/plantillas-obra");
        var totalSembradas = int.Parse(respTotal.Headers.GetValues("X-Total-Count").Single());
        Assert.True(totalSembradas >= 3);

        // Primera página con tamano=2
        var respPag1 = await client.GetAsync("/api/plantillas-obra?pagina=1&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respPag1.StatusCode);
        Assert.Equal(totalSembradas.ToString(), respPag1.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal("1", respPag1.Headers.GetValues("X-Page").Single());
        Assert.Equal("2", respPag1.Headers.GetValues("X-Page-Size").Single());
        var pag1 = await respPag1.Content.ReadFromJsonAsync<List<PlantillaObraResponseDto>>();
        Assert.NotNull(pag1);
        Assert.Equal(2, pag1.Count);

        // Última página
        int ultimaPagina = (int)Math.Ceiling(totalSembradas / 2.0);
        int esperadasEnUltima = totalSembradas - ((ultimaPagina - 1) * 2);

        var respUltima = await client.GetAsync($"/api/plantillas-obra?pagina={ultimaPagina}&tamano=2");
        Assert.Equal(HttpStatusCode.OK, respUltima.StatusCode);
        Assert.Equal(totalSembradas.ToString(), respUltima.Headers.GetValues("X-Total-Count").Single());
        Assert.Equal(ultimaPagina.ToString(), respUltima.Headers.GetValues("X-Page").Single());
        var pagUltima = await respUltima.Content.ReadFromJsonAsync<List<PlantillaObraResponseDto>>();
        Assert.NotNull(pagUltima);
        Assert.Equal(esperadasEnUltima, pagUltima.Count);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 51)]
    public async Task GetPlantillas_ParametrosFueraDeRango_Retorna400ProblemDetails(int pagina, int tamano)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "corr-plantillas-182");

        var response = await client.GetAsync($"/api/plantillas-obra?pagina={pagina}&tamano={tamano}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("Paginación inválida", problem.GetProperty("title").GetString());
        Assert.Equal("corr-plantillas-182", problem.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GetPlantillas_OrdenEstableConMismoNombre_DesempataPorIdSinRepetirNiPerderFilas()
    {
        _factory.CreateClient();
        var idsMismoNombre = new List<Guid>();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            for (int i = 0; i < 4; i++)
            {
                var p = new PlantillaObra
                {
                    Id = Guid.NewGuid(),
                    Nombre = "AAA Plantilla Empate Orden",
                    Descripcion = $"Variante {i}"
                };
                idsMismoNombre.Add(p.Id);
                context.PlantillasObra.Add(p);
            }
            await context.SaveChangesAsync();
        }

        idsMismoNombre.Sort();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateJwtToken(UserRoles.Admin));

        // Como el nombre empieza con "AAA ", estas 4 plantillas son las primeras del listado ordenado por Nombre
        var resp1 = await client.GetAsync("/api/plantillas-obra?pagina=1&tamano=2");
        var pag1 = await resp1.Content.ReadFromJsonAsync<List<PlantillaObraResponseDto>>();

        var resp2 = await client.GetAsync("/api/plantillas-obra?pagina=2&tamano=2");
        var pag2 = await resp2.Content.ReadFromJsonAsync<List<PlantillaObraResponseDto>>();

        Assert.NotNull(pag1);
        Assert.NotNull(pag2);

        var idsObtenidos = pag1.Select(x => x.Id).Concat(pag2.Select(x => x.Id)).ToList();
        Assert.Equal(idsMismoNombre, idsObtenidos);
    }
}