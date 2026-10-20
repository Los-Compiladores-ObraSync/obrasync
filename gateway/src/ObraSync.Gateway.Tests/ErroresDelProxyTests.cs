using System.Net;
using System.Text.Json;

namespace ObraSync.Gateway.Tests;

public class ErroresDelProxyTests : IClassFixture<GatewayFactory>
{
    private const string Ruta = "/api/usuarios/prueba";

    private readonly GatewayFactory _factory;

    public ErroresDelProxyTests(GatewayFactory factory)
    {
        _factory = factory;
        _factory.Destino.Reiniciar();
    }

    [Fact]
    public async Task Si_el_servicio_esta_caido_devuelve_502_con_ProblemDetails()
    {
        _factory.Destino.Falla = new HttpRequestException("no se pudo conectar");
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.BadGateway, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(502, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", json.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("detail").GetString()));
        Assert.Equal(Ruta, json.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task El_correlationId_del_cuerpo_coincide_con_el_encabezado()
    {
        _factory.Destino.Falla = new HttpRequestException("no se pudo conectar");
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        var idEncabezado = respuesta.Headers.GetValues("X-Correlation-ID").Single();
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(idEncabezado, json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Si_el_servicio_responde_con_error_su_respuesta_pasa_sin_cambios()
    {
        _factory.Destino.Estado = HttpStatusCode.InternalServerError;
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.NotEqual("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Con_el_servicio_funcionando_no_se_modifica_la_respuesta()
    {
        var client = _factory.CreateClient();

        var respuesta = await client.GetAsync(Ruta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
