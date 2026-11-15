using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Usuarios.Application.DTOs;
using ObraSync.Usuarios.Domain.Entities;
using ObraSync.Usuarios.Infrastructure.Data;
using Xunit;

namespace ObraSync.Usuarios.Tests.Integration;

public class LoginLockoutYPoliticaPasswordEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestJwtSecret = "ClaveSecretaDePruebaParaUsuariosObrasConMasDe32Caracteres!";
    private const string SeedPassword = "Password123!";
    private const string MensajeCredencialesInvalidas = "Credenciales inválidas.";
    private const string MensajeCuentaBloqueada =
        "La cuenta está bloqueada temporalmente por múltiples intentos fallidos. Intentá nuevamente más tarde.";

    private readonly WebApplicationFactory<Program> _factory;

    public LoginLockoutYPoliticaPasswordEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", TestJwtSecret);

        // Base de datos InMemory aislada por cada test para que los contadores de Lockout no interfieran entre pruebas
        var dbName = $"DB_Auth_Lockout_{Guid.NewGuid()}";

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
    public async Task Login_ConEmailInexistenteYConPasswordIncorrecta_RetornaMismoMensajeAntiEnumeracion()
    {
        using var client = _factory.CreateClient();

        // 1. Intento con email inexistente
        var correlationNoExiste = Guid.NewGuid().ToString();
        using var reqNoExiste = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
        {
            Content = JsonContent.Create(new LoginRequestDto("noexiste@obrasync.com", SeedPassword))
        };
        reqNoExiste.Headers.Add("X-Correlation-ID", correlationNoExiste);

        var respNoExiste = await client.SendAsync(reqNoExiste);
        var detailNoExiste = await AssertProblemDetailsConCorrelationIdAsync(
            respNoExiste, HttpStatusCode.Unauthorized, correlationNoExiste);

        // 2. Intento con email existente pero contraseña incorrecta
        var correlationPasswordMal = Guid.NewGuid().ToString();
        using var reqPasswordMal = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
        {
            Content = JsonContent.Create(new LoginRequestDto("capataz@obrasync.com", "ClaveIncorrecta999!"))
        };
        reqPasswordMal.Headers.Add("X-Correlation-ID", correlationPasswordMal);

        var respPasswordMal = await client.SendAsync(reqPasswordMal);
        var detailPasswordMal = await AssertProblemDetailsConCorrelationIdAsync(
            respPasswordMal, HttpStatusCode.Unauthorized, correlationPasswordMal);

        // Assert: ambos casos devuelven exactamente el mismo mensaje
        Assert.Equal(MensajeCredencialesInvalidas, detailNoExiste);
        Assert.Equal(MensajeCredencialesInvalidas, detailPasswordMal);
    }

    [Fact]
    public async Task Login_CincoIntentosFallidos_BloqueaLaCuentaEImpideLoginInclusoConPasswordCorrecta()
    {
        using var client = _factory.CreateClient();
        const string email = "capataz@obrasync.com";

        // 1. Primeros 4 intentos fallidos -> 401 con "Credenciales inválidas."
        for (int intento = 1; intento <= 4; intento++)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
            {
                Content = JsonContent.Create(new LoginRequestDto(email, $"ClaveErronea{intento}"))
            };
            req.Headers.Add("X-Correlation-ID", correlationId);

            var resp = await client.SendAsync(req);
            var detail = await AssertProblemDetailsConCorrelationIdAsync(resp, HttpStatusCode.Unauthorized, correlationId);
            Assert.Equal(MensajeCredencialesInvalidas, detail);
        }

        // 2. 5.º intento fallido -> alcanza MaxFailedAccessAttempts (5) y bloquea la cuenta
        var correlationQuinto = Guid.NewGuid().ToString();
        using var reqQuinto = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
        {
            Content = JsonContent.Create(new LoginRequestDto(email, "ClaveErronea5"))
        };
        reqQuinto.Headers.Add("X-Correlation-ID", correlationQuinto);

        var respQuinto = await client.SendAsync(reqQuinto);
        var detailQuinto = await AssertProblemDetailsConCorrelationIdAsync(
            respQuinto, HttpStatusCode.Unauthorized, correlationQuinto);
        Assert.Equal(MensajeCuentaBloqueada, detailQuinto);

        // 3. 6.º intento enviando la contraseña CORRECTA mientras la cuenta sigue bloqueada -> 401 Cuenta bloqueada
        var correlationSexto = Guid.NewGuid().ToString();
        using var reqSexto = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
        {
            Content = JsonContent.Create(new LoginRequestDto(email, SeedPassword))
        };
        reqSexto.Headers.Add("X-Correlation-ID", correlationSexto);

        var respSexto = await client.SendAsync(reqSexto);
        var detailSexto = await AssertProblemDetailsConCorrelationIdAsync(
            respSexto, HttpStatusCode.Unauthorized, correlationSexto);
        Assert.Equal(MensajeCuentaBloqueada, detailSexto);
    }

    [Fact]
    public async Task Login_CuandoExpiraElTiempoDeBloqueo_PermiteIniciarSesionNuevamente()
    {
        using var client = _factory.CreateClient();
        const string email = "comprador@obrasync.com";

        // 1. Provocar el bloqueo con 5 intentos fallidos
        for (int i = 1; i <= 5; i++)
        {
            var resp = await client.PostAsJsonAsync(
                "/api/usuarios/login",
                new LoginRequestDto(email, "ClaveErronea123!"));
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // 2. Simular expiración del bloqueo moviendo LockoutEnd al pasado
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.True(await userManager.IsLockedOutAsync(user));

            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        // 3. Intentar login con la contraseña correcta -> 200 OK con token JWT
        var loginResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, SeedPassword));

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var auth = await loginResp.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Login_ExitosoAntesDelLimite_ReiniciaContadorDeIntentosFallidosACero()
    {
        using var client = _factory.CreateClient();
        const string email = "proveedor@obrasync.com";

        // 1. Realizar 4 intentos fallidos (1 menos que el límite de 5)
        for (int i = 1; i <= 4; i++)
        {
            var resp = await client.PostAsJsonAsync(
                "/api/usuarios/login",
                new LoginRequestDto(email, "ClaveErronea123!"));
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // 2. Iniciar sesión con la contraseña correcta -> 200 OK y resetea AccessFailedCount a 0
        var loginExitoso = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, SeedPassword));
        Assert.Equal(HttpStatusCode.OK, loginExitoso.StatusCode);

        // 3. Otros 4 intentos fallidos seguidos NO deben bloquear la cuenta (porque el contador volvió a 0)
        for (int i = 1; i <= 4; i++)
        {
            var correlationId = Guid.NewGuid().ToString();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/usuarios/login")
            {
                Content = JsonContent.Create(new LoginRequestDto(email, "ClaveErronea123!"))
            };
            req.Headers.Add("X-Correlation-ID", correlationId);

            var resp = await client.SendAsync(req);
            var detail = await AssertProblemDetailsConCorrelationIdAsync(resp, HttpStatusCode.Unauthorized, correlationId);
            Assert.Equal(MensajeCredencialesInvalidas, detail);
        }

        // 4. Confirmar que la cuenta sigue desbloqueada iniciando sesión con éxito
        var segundoLoginExitoso = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto(email, SeedPassword));
        Assert.Equal(HttpStatusCode.OK, segundoLoginExitoso.StatusCode);
    }

    [Theory]
    [InlineData("Clave12", "8 caracteres")]
    [InlineData("ClaveSinNumeros!", "número")]
    public async Task Registro_ConPasswordDebil_Retorna400ConProblemDetails(string passwordInvalida, string fragmentoEsperado)
    {
        var correlationId = Guid.NewGuid().ToString();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/registro",
            new RegisterRequestDto(
                "Proveedor Nuevo",
                $"proveedor.{Guid.NewGuid():N}@test.com",
                passwordInvalida,
                UserRoles.Proveedor));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Contains(fragmentoEsperado, detail);
    }

    [Theory]
    [InlineData("Clave12", "8 caracteres")]
    [InlineData("ClaveSinNumeros!", "número")]
    public async Task RestablecerPassword_ConPasswordDebil_Retorna400ConProblemDetails(string passwordInvalida, string fragmentoEsperado)
    {
        using var client = _factory.CreateClient();
        const string email = "capataz@obrasync.com";

        // Generar un token real de restablecimiento desde UserManager
        string resetToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.NotNull(user);
            resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        }

        var correlationId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/password/restablecer",
            new RestablecerPasswordRequestDto(email, resetToken, passwordInvalida));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Contains(fragmentoEsperado, detail);
    }

    [Theory]
    [InlineData("Clave12", "8 caracteres")]
    [InlineData("ClaveSinNumeros!", "número")]
    public async Task CambiarPassword_ConPasswordDebil_Retorna400ConProblemDetails(string passwordInvalida, string fragmentoEsperado)
    {
        using var client = _factory.CreateClient();
        var loginResp = await client.PostAsJsonAsync(
            "/api/usuarios/login",
            new LoginRequestDto("capataz@obrasync.com", SeedPassword));
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        var auth = await loginResp.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.NotNull(auth);

        var correlationId = Guid.NewGuid().ToString();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var response = await client.PostAsJsonAsync(
            "/api/usuarios/cambiar-password",
            new CambiarPasswordRequestDto(SeedPassword, passwordInvalida, passwordInvalida));

        var detail = await AssertProblemDetailsConCorrelationIdAsync(response, HttpStatusCode.BadRequest, correlationId);
        Assert.Contains(fragmentoEsperado, detail);
    }
}
