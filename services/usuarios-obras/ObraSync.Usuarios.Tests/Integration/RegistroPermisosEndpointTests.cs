using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests.Integration;

public class RegistroPermisosEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosObrasConMasDe32Caracteres!";
    private const string SeedPassword = "Password123!";

    private readonly WebApplicationFactory<Program> _factory;

    public RegistroPermisosEndpointTests(WebApplicationFactory<Program> factory)
    {
        // Program.cs valida Jwt__Secret antes de builder.Build(); la fijamos en entorno y en UseSetting
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);

        var dbName = $"DB_Auth_Permisos_{Guid.NewGuid()}";

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

    private async Task<string> ObtenerTokenPorLoginRealAsync(string email)
    {
        using var client = _factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, SeedPassword));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var auth = await loginResp.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        return auth.Token;
    }

    private static async Task AssertProblemDetailsConCorrelationIdAsync(
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

    [Fact]
    public async Task Registro_SinToken_PideProveedor_Retorna201Created()
    {
        using var client = _factory.CreateClient();
        var email = $"prov-{Guid.NewGuid():N}@obrasync.com";

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto("Proveedor Público", email, "Password123!", UserRoles.Proveedor));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(email, dto.Email);
        Assert.Contains(UserRoles.Proveedor, dto.Roles);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Comprador)]
    public async Task Registro_SinToken_PideCapatazOComprador_Retorna403ConProblemDetails(string rolSolicitado)
    {
        var correlationId = Guid.NewGuid().ToString();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto("Intento Anónimo", $"anon-{Guid.NewGuid():N}@obrasync.com", "Password123!", rolSolicitado));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Forbidden, correlationId);
    }

    [Theory]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Proveedor)]
    public async Task Registro_ConTokenComprador_CreaCapatazYProveedor_Retorna201Created(string rolSolicitado)
    {
        var tokenComprador = await ObtenerTokenPorLoginRealAsync("comprador@obrasync.com");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenComprador);
        var email = $"alta-comp-{Guid.NewGuid():N}@obrasync.com";

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto($"Nuevo {rolSolicitado}", email, "Password123!", rolSolicitado));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(email, dto.Email);
        Assert.Contains(rolSolicitado, dto.Roles);
    }

    [Fact]
    public async Task Registro_ConTokenComprador_PideComprador_Retorna403ConProblemDetails()
    {
        var tokenComprador = await ObtenerTokenPorLoginRealAsync("comprador@obrasync.com");
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenComprador);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto("Otro Comprador", $"comp-{Guid.NewGuid():N}@obrasync.com", "Password123!", UserRoles.Comprador));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Forbidden, correlationId);
    }

    [Theory]
    [InlineData(UserRoles.Comprador)]
    [InlineData(UserRoles.Capataz)]
    [InlineData(UserRoles.Proveedor)]
    public async Task Registro_ConTokenAdmin_CreaCompradorCapatazYProveedor_Retorna201Created(string rolSolicitado)
    {
        var tokenAdmin = await ObtenerTokenPorLoginRealAsync("admin@obrasync.com");

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
        var email = $"alta-adm-{Guid.NewGuid():N}@obrasync.com";

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto($"Nuevo {rolSolicitado}", email, "Password123!", rolSolicitado));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal(email, dto.Email);
        Assert.Contains(rolSolicitado, dto.Roles);
    }

    [Fact]
    public async Task Registro_NadieCreaAdmin_Retorna403ConProblemDetails()
    {
        var tokenComprador = await ObtenerTokenPorLoginRealAsync("comprador@obrasync.com");
        var tokenAdmin = await ObtenerTokenPorLoginRealAsync("admin@obrasync.com");

        foreach (var token in new string?[] { null, tokenComprador, tokenAdmin })
        {
            var correlationId = Guid.NewGuid().ToString();
            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

            if (token != null)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var response = await client.PostAsJsonAsync(
                "/api/usuarios/registro",
                new RegisterRequestDto("Intento Admin", $"adm-{Guid.NewGuid():N}@obrasync.com", "Password123!", UserRoles.Admin));

            await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Forbidden, correlationId);
        }
    }

    [Theory]
    [InlineData("capataz@obrasync.com")]
    [InlineData("proveedor@obrasync.com")]
    public async Task Registro_ConTokenDeCapatazOProveedor_Retorna403ConProblemDetails(string emailOperativo)
    {
        var tokenOperativo = await ObtenerTokenPorLoginRealAsync(emailOperativo);
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenOperativo);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto("Intento Operativo", $"op-{Guid.NewGuid():N}@obrasync.com", "Password123!", UserRoles.Proveedor));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Forbidden, correlationId);
    }

    [Fact]
    public async Task Registro_ConTokenInvalido_Retorna401ConProblemDetails()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token.jwt.invalido");

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto("Proveedor", $"prov-{Guid.NewGuid():N}@obrasync.com", "Password123!", UserRoles.Proveedor));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Unauthorized, correlationId);
    }
}
