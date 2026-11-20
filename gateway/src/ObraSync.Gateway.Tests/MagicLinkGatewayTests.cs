using System.Net;

namespace ObraSync.Gateway.Tests;

public class MagicLinkGatewayTests : IClassFixture<GatewayFactory>
{
    private readonly GatewayFactory _factory;
    private const string Hash = "token-secreto-159";
    private const string Ruta = "/api/licitaciones/magic-links/" + Hash + "/cotizar";

    public MagicLinkGatewayTests(GatewayFactory factory) => _factory = factory;

    private HttpRequestMessage CrearPedido(string ip, string? forwardedFor = null, string? hash = null)
    {
        var ruta = hash is null ? Ruta : $"/api/licitaciones/magic-links/{hash}/cotizar";
        var request = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = new StringContent("{}") };
        if (!IPAddress.TryParse(ip, out _))
        {
            var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(ip));
            ip = $"198.18.{bytes[0]}.{bytes[1]}";
        }
        request.Headers.Add("X-Test-Remote-IP", ip);
        request.Headers.Add("X-Correlation-ID", "magic-test-159");
        if (forwardedFor is not null) request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }

    [Fact]
    public async Task Post_del_magic_link_se_reenvia_a_licitaciones_sin_jwt()
    {
        _factory.Destino.Reiniciar();
        using var client = _factory.CreateClient();
        using var request = CrearPedido("192.0.2.10");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(request.Headers.Authorization);
        Assert.Equal(5003, _factory.Destino.UltimoPedido?.Uri.Port);
        Assert.Equal(Ruta, _factory.Destino.UltimoPedido?.Uri.AbsolutePath);
        Assert.DoesNotContain(_factory.Logs.Entradas, EsEventoForwarding);
        Assert.Contains(_factory.Logs.Entradas, e => EsHttpForwarder(e) && e.EventId.Id == 56);
    }

    [Fact]
    public async Task Sexta_solicitud_por_ip_devuelve_429_sin_reenviarse()
    {
        _factory.Destino.Reiniciar();
        using var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var ok = await client.SendAsync(CrearPedido("192.0.2.20"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rechazada = await client.SendAsync(CrearPedido("192.0.2.20"));

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("application/problem+json", rechazada.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(Hash, await rechazada.Content.ReadAsStringAsync());
        Assert.Equal(5, _factory.Destino.CantidadPedidos);
        Assert.Equal(Ruta, _factory.Destino.UltimoPedido?.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Dos_ips_remotas_tienen_particiones_independientes()
    {
        _factory.Destino.Reiniciar();
        using var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.SendAsync(CrearPedido("192.0.2.30"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var otraIp = await client.SendAsync(CrearPedido("192.0.2.31"));

        Assert.Equal(HttpStatusCode.OK, otraIp.StatusCode);
    }

    [Fact]
    public async Task X_forwarded_for_del_cliente_no_cambia_la_particion()
    {
        _factory.Destino.Reiniciar();
        using var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.SendAsync(CrearPedido("192.0.2.40", $"198.51.100.{i + 1}"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var sexta = await client.SendAsync(CrearPedido("192.0.2.40", "203.0.113.99"));

        Assert.Equal(HttpStatusCode.TooManyRequests, sexta.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null)]
    [InlineData(null, "fallo de transporte")]
    public async Task Hash_no_aparece_en_logs_con_respuestas_de_error_o_fallos(HttpStatusCode? status, string? error)
    {
        var hash = Guid.NewGuid().ToString("N");
        _factory.Destino.Reiniciar();
        _factory.Logs.Limpiar();
        _factory.Destino.Estado = status ?? HttpStatusCode.OK;
        _factory.Destino.Falla = error is null ? null : new HttpRequestException(error);
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(CrearPedido(Guid.NewGuid().ToString("N"), hash: hash));

        Assert.DoesNotContain(hash, _factory.Logs.Texto, StringComparison.Ordinal);
        Assert.Contains("/api/licitaciones/magic-links/{hash}/cotizar", _factory.Logs.Texto);
        Assert.Contains("magic-test-159", _factory.Logs.Texto);
        Assert.DoesNotContain(_factory.Logs.Entradas, EsEventoForwarding);
        if (error is null)
            Assert.Contains(_factory.Logs.Entradas, e => EsHttpForwarder(e) && e.EventId.Id == 56);
        else
            Assert.Contains(_factory.Logs.Entradas, e => EsHttpForwarder(e) && e.EventId.Name == "ForwardingError");
    }

    [Fact]
    public async Task Hash_no_aparece_en_logs_ni_en_url_saneada_de_exito()
    {
        var hash = Guid.NewGuid().ToString("N");
        _factory.Destino.Reiniciar();
        _factory.Logs.Limpiar();
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(CrearPedido(Guid.NewGuid().ToString("N"), hash: hash));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(hash, _factory.Logs.Texto, StringComparison.Ordinal);
        Assert.Contains("POST", _factory.Logs.Texto);
        Assert.Contains("forwarded", _factory.Logs.Texto);
        Assert.DoesNotContain(_factory.Logs.Entradas, EsEventoForwarding);
        Assert.Contains(_factory.Logs.Entradas, e => EsHttpForwarder(e) && e.EventId.Id == 56);
    }

    [Fact]
    public async Task Otras_rutas_conservan_el_evento_de_destino_de_Yarp()
    {
        _factory.Destino.Reiniciar();
        _factory.Logs.Limpiar();
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/licitaciones/prueba");
        request.Headers.Add("X-Test-Remote-IP", Guid.NewGuid().ToString("N"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/api/licitaciones/prueba", _factory.Destino.UltimoPedido?.Uri.AbsolutePath);
        Assert.Contains(_factory.Logs.Entradas, EsEventoForwarding);
        Assert.Contains(_factory.Logs.Entradas, e => EsHttpForwarder(e) && e.EventId.Id == 56);
    }

    [Fact]
    public async Task Correlation_id_se_propaga_en_respuesta_y_a_licitaciones()
    {
        _factory.Destino.Reiniciar();
        using var client = _factory.CreateClient();

        using var response = await client.SendAsync(CrearPedido(Guid.NewGuid().ToString("N")));

        Assert.Equal("magic-test-159", response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("magic-test-159", _factory.Destino.UltimoPedido?.CorrelationId);
    }

    private static bool EsHttpForwarder(EntradaLog entrada) =>
        entrada.Categoria == "Yarp.ReverseProxy.Forwarder.HttpForwarder";

    private static bool EsEventoForwarding(EntradaLog entrada) =>
        EsHttpForwarder(entrada) && entrada.EventId.Id == 9;
}
