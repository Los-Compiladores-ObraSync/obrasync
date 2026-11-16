using System.Net;
using System.Text;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>El lector de requisiciones: solo GET, siempre con el JWT del usuario y con errores estables.</summary>
public class RequisicionesGatewayTests
{
    private const string Id1 = "11111111-1111-1111-1111-111111111111";
    private const string ObraId = "22222222-2222-2222-2222-222222222222";

    private const string JsonLista =
        "[{\"requisicionId\":\"" + Id1 + "\",\"numeroRequisicion\":\"REQ-0001\",\"obraId\":\"" + ObraId + "\"," +
        "\"etapa\":\"Estructura\",\"estado\":\"Pendiente\",\"fechaCreacion\":\"2026-11-10T14:30:00Z\"," +
        "\"items\":[{\"articuloId\":\"33333333-3333-3333-3333-333333333333\",\"descripcion\":\"Cemento\",\"cantidad\":20.5,\"unidadMedida\":\"BOLSA\"}]," +
        "\"historialRechazos\":[],\"campoNuevoQueNoConocemos\":1}]";

    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private static RequisicionesGateway Armar(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso { Rol = "Comprador" });

    /// <summary>Gateway falso que además devuelve el encabezado X-Total-Count.</summary>
    private sealed class HandlerConTotal : HttpMessageHandler
    {
        private readonly string _cuerpo;
        private readonly string? _total;
        public string? Url { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }

        public HandlerConTotal(string cuerpo, string? total)
        {
            _cuerpo = cuerpo;
            _total = total;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Url = req.RequestUri!.AbsoluteUri;
            Metodo = req.Method;
            Authorization = req.Headers.Authorization?.ToString();
            CorrelationId = req.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json")
            };
            if (_total is not null)
                resp.Headers.Add("X-Total-Count", _total);
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task Listar_hace_un_GET_con_el_JWT_el_correlation_id_y_la_paginacion()
    {
        var handler = new HandlerConTotal(JsonLista, "12");
        var contexto = new ContextoFalso { Jwt = "JWT_DEL_COMPRADOR", CorrelationId = "abc-123", Rol = "Comprador" };

        await Armar(handler, contexto).ListarAsync("Pendiente", 5, default);

        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Equal("http://gateway.test:5000/api/requisiciones?pagina=1&tamano=5&estado=Pendiente", handler.Url);
        Assert.Equal("Bearer JWT_DEL_COMPRADOR", handler.Authorization);
        Assert.Equal("abc-123", handler.CorrelationId);
    }

    [Fact]
    public async Task Listar_sin_estado_no_manda_el_filtro()
    {
        var handler = new HandlerConTotal(JsonLista, null);

        await Armar(handler).ListarAsync(null, 5, default);

        Assert.Equal("http://gateway.test:5000/api/requisiciones?pagina=1&tamano=5", handler.Url);
    }

    [Fact]
    public async Task Listar_parsea_la_lista_el_total_e_ignora_los_campos_desconocidos()
    {
        var r = await Armar(new HandlerConTotal(JsonLista, "12")).ListarAsync(null, 5, default);

        Assert.Null(r.Error);
        Assert.Equal(12, r.Total);
        var req = Assert.Single(r.Requisiciones!);
        Assert.Equal(Guid.Parse(Id1), req.Id);
        Assert.Equal("REQ-0001", req.Numero);
        Assert.Equal("Pendiente", req.Estado);
        Assert.Equal(Guid.Parse(ObraId), req.ObraId);
        Assert.Equal("Estructura", req.Etapa);
        Assert.Equal(1, req.CantidadItems);
    }

    [Fact]
    public async Task Listar_sin_el_encabezado_de_total_deja_el_total_sin_definir()
    {
        var r = await Armar(new HandlerConTotal(JsonLista, null)).ListarAsync(null, 5, default);

        Assert.Null(r.Error);
        Assert.Null(r.Total);
    }

    [Fact]
    public async Task Obtener_hace_un_GET_al_detalle_y_trae_los_materiales()
    {
        var handler = new HandlerConTotal(JsonLista.Trim('[', ']'), null);

        var r = await Armar(handler).ObtenerAsync(Guid.Parse(Id1), default);

        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Equal($"http://gateway.test:5000/api/requisiciones/{Id1}", handler.Url);
        Assert.Null(r.Error);
        var item = Assert.Single(r.Requisicion!.Items);
        Assert.Equal("Cemento", item.Descripcion);
        Assert.Equal(20.5m, item.Cantidad);
        Assert.Equal("BOLSA", item.UnidadMedida);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API_y_devuelve_sin_permiso()
    {
        var handler = new HandlerCatalogo(200, JsonLista);

        var lista = await Armar(handler, new ContextoFalso { Jwt = null }).ListarAsync(null, 5, default);
        var detalle = await Armar(handler, new ContextoFalso { Jwt = null }).ObtenerAsync(Guid.NewGuid(), default);

        Assert.Equal("sin_permiso", lista.Error);
        Assert.Equal("sin_permiso", detalle.Error);
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData(401, "sin_permiso")]
    [InlineData(403, "sin_permiso")]
    [InlineData(404, "requisicion_no_encontrada")]
    [InlineData(500, "api_http_error")]
    public async Task Los_codigos_HTTP_de_la_API_se_traducen_a_errores_estables(int estado, string esperado)
    {
        var lista = await Armar(new HandlerCatalogo(estado, "{}")).ListarAsync(null, 5, default);
        var detalle = await Armar(new HandlerCatalogo(estado, "{}")).ObtenerAsync(Guid.NewGuid(), default);

        Assert.Equal(esperado, lista.Error);
        Assert.Equal(esperado, detalle.Error);
    }

    [Fact]
    public async Task Una_respuesta_demasiado_grande_no_se_le_pasa_al_modelo()
    {
        var enorme = "[" + new string(' ', RequisicionesGateway.MaxBytes + 1) + "]";

        var r = await Armar(new HandlerCatalogo(200, enorme)).ListarAsync(null, 5, default);

        Assert.Equal("respuesta_demasiado_grande", r.Error);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_JSON_devuelve_respuesta_invalida()
    {
        var r = await Armar(new HandlerCatalogo(200, "<html>no soy json</html>")).ListarAsync(null, 5, default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    [Fact]
    public async Task Si_el_Gateway_esta_caido_devuelve_api_no_disponible()
    {
        var r = await Armar(new HandlerCatalogo(200, "[]", lanzaError: true)).ListarAsync(null, 5, default);

        Assert.Equal("api_no_disponible", r.Error);
    }

    [Fact]
    public async Task Si_la_API_demora_mas_del_limite_devuelve_api_timeout()
    {
        var handler = new HandlerCatalogo(200, "[]", demora: RequisicionesGateway.TiempoApi + TimeSpan.FromSeconds(2));

        var r = await Armar(handler).ListarAsync(null, 5, default);

        Assert.Equal("api_timeout", r.Error);
    }
}
