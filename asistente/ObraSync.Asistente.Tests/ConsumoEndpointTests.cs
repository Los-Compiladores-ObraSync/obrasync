using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ObraSync.Asistente.Application;

namespace ObraSync.Asistente.Tests;

/// <summary>Control de consumo con una decisión fija: sirve para probar cómo responde el endpoint.</summary>
internal sealed class ControlFijo : IControlConsumo
{
    private readonly DecisionConsumo _decision;
    public ControlFijo(DecisionConsumo decision) => _decision = decision;
    public DecisionConsumo Autorizar() => _decision;
    public void Registrar(int tokens) { }
    public void ReportarLimiteDeTasa() { }
    public void ReportarSinCupo() { }
    public EstadoConsumo Estado() => new(true, false, false, 1_500, 10_000, 250_000, 18_750_000, 1.33, false);
}

/// <summary>Endpoint completo con el control de consumo: respuestas 429/503 sin llamar al modelo y estado solo para Admin.</summary>
public class ConsumoEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public ConsumoEndpointTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Secret", AsistenteEndpointTests.Secreto));
    }

    private HttpClient Cliente(IModeloChat modelo, IControlConsumo control) =>
        _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IModeloChat>();
            s.AddScoped(_ => modelo);
            s.RemoveAll<IBuscadorInsumos>();
            s.AddScoped<IBuscadorInsumos>(_ => new BuscadorFalso());
            s.RemoveAll<IBuscadorObras>();
            s.AddScoped<IBuscadorObras>(_ => new ObrasFalsas());
            s.RemoveAll<IControlConsumo>();
            s.AddSingleton(control);
        })).CreateClient();

    private static HttpRequestMessage Chat(string rol) => Pedido(HttpMethod.Post, "/api/asistente/chat", rol, new { mensaje = "hola" });

    private static HttpRequestMessage Pedido(HttpMethod metodo, string ruta, string? rol, object? cuerpo = null)
    {
        var pedido = new HttpRequestMessage(metodo, ruta);
        if (cuerpo is not null) pedido.Content = JsonContent.Create(cuerpo);
        if (rol is not null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AsistenteEndpointTests.CrearToken(rol));
        return pedido;
    }

    [Fact]
    public async Task Cerca_del_tope_el_chat_responde_429_con_Retry_After_y_sin_llamar_al_modelo()
    {
        var modelo = new ModeloFalso();
        var cliente = Cliente(modelo, new ControlFijo(DecisionConsumo.Ocupado));

        var r = await cliente.SendAsync(Chat("Capataz"));

        Assert.Equal(HttpStatusCode.TooManyRequests, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("60", r.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.Empty(modelo.Pedidos);
    }

    [Theory]
    [InlineData(DecisionConsumo.Apagado)]
    [InlineData(DecisionConsumo.SinCupo)]
    public async Task Apagado_o_sin_cupo_el_chat_responde_503_y_no_llama_al_modelo(DecisionConsumo decision)
    {
        var modelo = new ModeloFalso();
        var cliente = Cliente(modelo, new ControlFijo(decision));

        var r = await cliente.SendAsync(Chat("Comprador"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Empty(modelo.Pedidos);
    }

    [Fact]
    public async Task El_Admin_ve_el_estado_del_consumo()
    {
        var cliente = Cliente(new ModeloFalso(), new ControlFijo(DecisionConsumo.Permitido));

        var r = await cliente.SendAsync(Pedido(HttpMethod.Get, "/api/asistente/estado", "Admin"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(250_000, json.GetProperty("tokensMes").GetInt64());
        Assert.Equal(10_000, json.GetProperty("topeMinuto").GetInt32());
        Assert.True(json.GetProperty("habilitado").GetBoolean());
    }

    [Theory]
    [InlineData("Capataz")]
    [InlineData("Comprador")]
    [InlineData("Proveedor")]
    public async Task Los_demas_roles_no_ven_el_estado(string rol)
    {
        var cliente = Cliente(new ModeloFalso(), new ControlFijo(DecisionConsumo.Permitido));

        var r = await cliente.SendAsync(Pedido(HttpMethod.Get, "/api/asistente/estado", rol));

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Sin_token_el_estado_devuelve_401()
    {
        var cliente = Cliente(new ModeloFalso(), new ControlFijo(DecisionConsumo.Permitido));

        var r = await cliente.SendAsync(Pedido(HttpMethod.Get, "/api/asistente/estado", null));

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}

/// <summary>Límite por usuario (#144): cada persona puede mandar N mensajes por hora; las demás no se ven afectadas.</summary>
public class LimitePorUsuarioTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _fabrica;

    public LimitePorUsuarioTests(WebApplicationFactory<Program> fabrica)
    {
        _fabrica = fabrica.WithWebHostBuilder(b =>
        {
            b.UseSetting("Jwt:Secret", AsistenteEndpointTests.Secreto);
            b.UseSetting("Consumo:MensajesPorHoraPorUsuario", "2");
        });
    }

    private (HttpClient Cliente, ModeloFalso Modelo) Armar()
    {
        var modelo = new ModeloFalso(
            _ => Respuestas.Final("uno"), _ => Respuestas.Final("dos"), _ => Respuestas.Final("tres"));
        var cliente = _fabrica.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IModeloChat>();
            s.AddScoped<IModeloChat>(_ => modelo);
            s.RemoveAll<IBuscadorInsumos>();
            s.AddScoped<IBuscadorInsumos>(_ => new BuscadorFalso());
            s.RemoveAll<IBuscadorObras>();
            s.AddScoped<IBuscadorObras>(_ => new ObrasFalsas());
        })).CreateClient();
        return (cliente, modelo);
    }

    private static HttpRequestMessage Chat(string token)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/asistente/chat") { Content = JsonContent.Create(new { mensaje = "hola" }) };
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return pedido;
    }

    [Fact]
    public async Task Pasado_el_limite_por_hora_el_usuario_recibe_429_sin_llamar_al_modelo()
    {
        var (cliente, modelo) = Armar();
        var token = AsistenteEndpointTests.CrearToken("Capataz");

        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Chat(token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Chat(token))).StatusCode);
        var tercero = await cliente.SendAsync(Chat(token));

        Assert.Equal(HttpStatusCode.TooManyRequests, tercero.StatusCode);
        Assert.Equal("application/problem+json", tercero.Content.Headers.ContentType?.MediaType);
        Assert.True(tercero.Headers.RetryAfter?.Delta?.TotalSeconds > 0);
        var json = JsonDocument.Parse(await tercero.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(429, json.GetProperty("status").GetInt32());
        Assert.True(json.TryGetProperty("correlationId", out _));
        Assert.Equal(2, modelo.Pedidos.Count);          // el tercer mensaje no llegó al modelo
    }

    [Fact]
    public async Task El_limite_es_por_usuario_y_otro_usuario_sigue_pudiendo_escribir()
    {
        var (cliente, _) = Armar();
        var primero = AsistenteEndpointTests.CrearToken("Capataz");
        var otro = AsistenteEndpointTests.CrearToken("Comprador");     // otro "sub"

        await cliente.SendAsync(Chat(primero));
        await cliente.SendAsync(Chat(primero));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await cliente.SendAsync(Chat(primero))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await cliente.SendAsync(Chat(otro))).StatusCode);
    }

    [Fact]
    public async Task Un_pedido_sin_token_recibe_401_y_no_gasta_el_limite_de_nadie()
    {
        var (cliente, _) = Armar();
        var sinToken = new HttpRequestMessage(HttpMethod.Post, "/api/asistente/chat") { Content = JsonContent.Create(new { mensaje = "hola" }) };

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.SendAsync(sinToken.Clone())).StatusCode);
    }
}

internal static class PedidoExtensiones
{
    /// <summary>Un HttpRequestMessage solo se puede mandar una vez: se arma una copia.</summary>
    public static HttpRequestMessage Clone(this HttpRequestMessage original)
    {
        var copia = new HttpRequestMessage(original.Method, original.RequestUri) { Content = JsonContent.Create(new { mensaje = "hola" }) };
        return copia;
    }
}
