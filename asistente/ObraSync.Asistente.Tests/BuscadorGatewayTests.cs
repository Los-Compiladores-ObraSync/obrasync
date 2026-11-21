using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

public class BuscadorGatewayTests
{
    private const string JsonCatalogo =
        "[{\"articuloId\":\"11111111-1111-1111-1111-111111111111\",\"codigo\":\"MAT-CEM-001\",\"descripcion\":\"Cemento Portland 25 kg\",\"unidadMedida\":\"BOLSA\",\"moneda\":\"ARS\",\"activo\":true,\"campoNuevoQueNoConocemos\":123}]";

    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private static BuscadorGateway Armar(HandlerCatalogo handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso());

    [Fact]
    public async Task Manda_el_JWT_del_usuario_y_el_correlation_id_del_pedido()
    {
        var handler = new HandlerCatalogo(200, JsonCatalogo);
        var contexto = new ContextoFalso { Jwt = "JWT_DEL_USUARIO", CorrelationId = "abc-123" };

        await Armar(handler, contexto).BuscarAsync("cemento", default);

        Assert.Equal("Bearer JWT_DEL_USUARIO", handler.Authorization);
        Assert.Equal("abc-123", handler.CorrelationId);
    }

    [Fact]
    public async Task Arma_la_URL_con_el_texto_escapado_y_solo_activos()
    {
        var handler = new HandlerCatalogo(200, JsonCatalogo);

        await Armar(handler).BuscarAsync("cemento y arena", default);

        Assert.Equal("http://gateway.test:5000/api/catalogo/insumos?search=cemento%20y%20arena&soloActivos=true&pagina=1&tamano=5", handler.Url);
    }

    [Fact]
    public async Task Parsea_el_insumo_e_ignora_los_campos_desconocidos()
    {
        var r = await Armar(new HandlerCatalogo(200, JsonCatalogo)).BuscarAsync("cemento", default);

        Assert.Null(r.Error);
        var insumo = Assert.Single(r.Insumos!);
        Assert.Equal("MAT-CEM-001", insumo.Codigo);
        Assert.Equal("BOLSA", insumo.UnidadMedida);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API_y_devuelve_sin_permiso()
    {
        var handler = new HandlerCatalogo(200, JsonCatalogo);

        var r = await Armar(handler, new ContextoFalso { Jwt = null }).BuscarAsync("cemento", default);

        Assert.Equal("sin_permiso", r.Error);
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData(401, "sin_permiso")]
    [InlineData(403, "sin_permiso")]
    [InlineData(500, "api_http_error")]
    public async Task Los_codigos_HTTP_de_la_API_se_traducen_a_errores_estables(int estado, string esperado)
    {
        var r = await Armar(new HandlerCatalogo(estado, "{}")).BuscarAsync("x", default);

        Assert.Equal(esperado, r.Error);
    }

    [Fact]
    public async Task Una_respuesta_enorme_se_rechaza()
    {
        var enorme = new string('a', BuscadorGateway.MaxBytes + 1);

        var r = await Armar(new HandlerCatalogo(200, enorme)).BuscarAsync("x", default);

        Assert.Equal("respuesta_demasiado_grande", r.Error);
    }

    [Fact]
    public async Task Un_JSON_roto_devuelve_respuesta_invalida()
    {
        var r = await Armar(new HandlerCatalogo(200, "esto no es json")).BuscarAsync("x", default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    [Fact]
    public async Task Si_el_Gateway_esta_caido_devuelve_api_no_disponible()
    {
        var r = await Armar(new HandlerCatalogo(0, "", lanzaError: true)).BuscarAsync("x", default);

        Assert.Equal("api_no_disponible", r.Error);
    }

    [Fact]
    public async Task Al_modelo_solo_le_llegan_los_primeros_resultados()
    {
        var muchos = "[" + string.Join(",", Enumerable.Range(1, 8).Select(n =>
            $"{{\"articuloId\":\"11111111-1111-1111-1111-{n:D12}\",\"codigo\":\"C{n}\",\"descripcion\":\"Insumo {n}\",\"unidadMedida\":\"UNIDAD\"}}")) + "]";

        var r = await Armar(new HandlerCatalogo(200, muchos)).BuscarAsync("x", default);

        Assert.Equal(BuscadorGateway.MaxResultados, r.Insumos!.Count);
    }

    [Fact]
    public async Task Una_API_que_no_responde_se_corta_por_timeout_y_devuelve_api_timeout()
    {
        var handler = new HandlerCatalogo(200, "[]", demora: TimeSpan.FromSeconds(30));
        var reloj = System.Diagnostics.Stopwatch.StartNew();

        var r = await Armar(handler).BuscarAsync("x", default);

        Assert.Equal("api_timeout", r.Error);
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10));
    }
}
