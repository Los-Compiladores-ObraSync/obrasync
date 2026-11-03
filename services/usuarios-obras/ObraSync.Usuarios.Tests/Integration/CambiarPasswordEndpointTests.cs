using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests.Integration;

public class CambiarPasswordEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosObrasConMasDe32Caracteres!";
    private const string SeedPassword = "Password123!";

    private readonly WebApplicationFactory<Program> _factory;

    public CambiarPasswordEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);

        // Base de datos InMemory única por cada prueba para que el cambio de clave no afecte a otros tests
        var dbName = $"DB_Auth_CambiarPassword_{Guid.NewGuid()}";

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

    private async Task<string> ObtenerTokenPorLoginRealAsync(string email, string password = SeedPassword)
    {
        using var client = _factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, password));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var auth = await loginResp.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));

        return auth.Token;
    }

    private static async Task<string> AssertProblemDetailsConCorrelationIdAsync(
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
        var detail = root.GetProperty("detail").GetString();
        Assert.False(string.IsNullOrWhiteSpace(detail));
        Assert.Equal(expectedCorrelationId, root.GetProperty("correlationId").GetString());

        return detail!;
    }

    [Fact]
    public async Task CambiarPassword_SinToken_Retorna401ConProblemDetails()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, "NuevaClave456!", "NuevaClave456!"));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Unauthorized, correlationId);
    }

    [Fact]
    public async Task CambiarPassword_ConTokenInvalido_Retorna401ConProblemDetails()
    {
        var correlationId = Guid.NewGuid().ToString();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token.jwt.invalido");

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, "NuevaClave456!", "NuevaClave456!"));

        await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.Unauthorized, correlationId);
    }

    [Fact]
    public async Task CambiarPassword_ConPasswordActualIncorrecta_Retorna400ConProblemDetails()
    {
        var token = await ObtenerTokenPorLoginRealAsync("capataz@obrasync.com");
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto("ClaveActualErronea!", "NuevaClave456!", "NuevaClave456!"));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Equal("La contraseña actual es incorrecta.", detail);
    }

    [Fact]
    public async Task CambiarPassword_ConNuevaPasswordIgualALaActual_Retorna400ConProblemDetails()
    {
        var token = await ObtenerTokenPorLoginRealAsync("capataz@obrasync.com");
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, SeedPassword, SeedPassword));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Equal("La nueva contraseña no puede ser igual a la contraseña actual.", detail);
    }

    [Fact]
    public async Task CambiarPassword_ConConfirmacionQueNoCoincide_Retorna400ConProblemDetails()
    {
        var token = await ObtenerTokenPorLoginRealAsync("capataz@obrasync.com");
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, "NuevaClave456!", "ConfirmacionDistinta!"));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Equal("La nueva contraseña y su confirmación no coinciden.", detail);
    }

    [Fact]
    public async Task CambiarPassword_ConNuevaPasswordMenorA6Caracteres_Retorna400ConProblemDetails()
    {
        var token = await ObtenerTokenPorLoginRealAsync("capataz@obrasync.com");
        var correlationId = Guid.NewGuid().ToString();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, "12345", "12345"));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Contains("6 caracteres", detail);
    }

    [Theory]
    [InlineData("capataz@obrasync.com")]
    [InlineData("comprador@obrasync.com")]
    [InlineData("proveedor@obrasync.com")]
    [InlineData("admin@obrasync.com")]
    public async Task CambiarPassword_CircuitoCompletoExitoso_InvalidaClaveViejaYPermiteLoginConClaveNueva(string email)
    {
        // 1. Iniciar sesión con la contraseña actual
        var token = await ObtenerTokenPorLoginRealAsync(email, SeedPassword);
        const string nuevaPassword = "NuevaPassword2026!";

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 2. Cambiar contraseña con sesión iniciada -> 200 OK
        var cambiarResp = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, nuevaPassword, nuevaPassword));

        Assert.Equal(HttpStatusCode.OK, cambiarResp.StatusCode);
        var dto = await cambiarResp.Content.ReadFromJsonAsync<MensajeResponseDto>();
        Assert.NotNull(dto);
        Assert.Equal("Contraseña actualizada con éxito.", dto.Mensaje);

        // 3. Verificar que la contraseña vieja ya NO permite iniciar sesión -> 401 Unauthorized
        var loginViejoResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, SeedPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, loginViejoResp.StatusCode);

        // 4. Verificar que la nueva contraseña SÍ permite iniciar sesión -> 200 OK
        var loginNuevoResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, nuevaPassword));
        Assert.Equal(HttpStatusCode.OK, loginNuevoResp.StatusCode);
    }
}
