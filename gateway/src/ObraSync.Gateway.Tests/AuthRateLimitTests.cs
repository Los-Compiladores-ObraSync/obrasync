using System.Net;

namespace ObraSync.Gateway.Tests;

public class AuthRateLimitTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;
    public AuthRateLimitTests(GatewayFactory factory) => _factory = factory;

    private static HttpRequestMessage Pedido(HttpMethod metodo, string ruta, string ip)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(ip));
        var request = new HttpRequestMessage(metodo, ruta) { Content = new StringContent("{}") };
        request.Headers.Add("X-Test-Remote-IP", $"198.19.{bytes[0]}.{bytes[1]}");
        return request;
    }

    [Theory]
    [InlineData("/api/usuarios/login")]
    [InlineData("/api/usuarios/registro")]
    [InlineData("/api/usuarios/password/recuperar")]
    [InlineData("/api/usuarios/password/restablecer")]
    public async Task Pasado_el_limite_devuelve_429_con_ProblemDetails(string ruta)
    {
        using var client = _factory.CreateClient();
        var ip = "auth-" + ruta;

        for (var i = 0; i < 10; i++)
        {
            using var ok = await client.SendAsync(Pedido(HttpMethod.Post, ruta, ip));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, ok.StatusCode);
        }

        using var bloqueada = await client.SendAsync(Pedido(HttpMethod.Post, ruta, ip));
        Assert.Equal(HttpStatusCode.TooManyRequests, bloqueada.StatusCode);
        Assert.Equal("application/problem+json", bloqueada.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Otra_IP_no_se_ve_afectada()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 11; i++)
            using (await client.SendAsync(Pedido(HttpMethod.Post, "/api/usuarios/login", "ip-saturada"))) { }

        using var otra = await client.SendAsync(Pedido(HttpMethod.Post, "/api/usuarios/login", "ip-distinta"));
        Assert.NotEqual(HttpStatusCode.TooManyRequests, otra.StatusCode);
    }

    [Fact]
    public async Task Las_rutas_autenticadas_de_Usuarios_no_se_limitan()
    {
        using var client = _factory.CreateClient();
        for (var i = 0; i < 15; i++)
        {
            using var r = await client.SendAsync(Pedido(HttpMethod.Get, "/api/usuarios/obras", "ip-libre"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode);
        }
    }
}
