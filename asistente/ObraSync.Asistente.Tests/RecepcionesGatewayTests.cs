using System.Net;
using System.Text;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>El lector de órdenes pendientes (#147): solo GET, siempre con el JWT del usuario y con errores estables.</summary>
public class RecepcionesGatewayTests
{
    private const string OrdenId = "11111111-1111-1111-1111-111111111111";
    private const string ObraId = "22222222-2222-2222-2222-222222222222";

    private const string JsonLista =
        "[{\"ordenCompraId\":\"" + OrdenId + "\",\"obraId\":\"" + ObraId + "\",\"etapa\":\"Estructura\",\"sinPartidaPresupuestaria\":false," +
        "\"items\":[{\"articuloId\":\"33333333-3333-3333-3333-333333333333\",\"descripcionArticulo\":\"Cemento\"," +
        "\"cantidadEsperada\":80,\"cantidadRecibida\":0,\"unidadMedida\":\"BOLSA\",\"campoNuevo\":1}]}]";

    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _estado;
        private readonly string _cuerpo;
        private readonly string? _total;
        private readonly Exception? _falla;
        public string? Url { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }

        public Handler(string cuerpo, string? total = null, HttpStatusCode estado = HttpStatusCode.OK, Exception? falla = null)
        {
            _cuerpo = cuerpo;
            _total = total;
            _estado = estado;
            _falla = falla;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Url = req.RequestUri!.AbsoluteUri;
            Metodo = req.Method;
            Authorization = req.Headers.Authorization?.ToString();
            CorrelationId = req.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;
            if (_falla is not null)
                throw _falla;
            var resp = new HttpResponseMessage(_estado) { Content = new StringContent(_cuerpo, Encoding.UTF8, "application/json") };
            if (_total is not null)
                resp.Headers.Add("X-Total-Count", _total);
            return Task.FromResult(resp);
        }
    }

    private static RecepcionesGateway Armar(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso { Rol = "Capataz" });

    [Fact]
    public async Task Hace_un_GET_con_el_JWT_el_correlation_id_y_la_paginacion()
    {
        var handler = new Handler(JsonLista, "7");
        var contexto = new ContextoFalso { Jwt = "JWT_DEL_CAPATAZ", CorrelationId = "abc-123", Rol = "Capataz" };

        var r = await Armar(handler, contexto).ListarPendientesAsync(5, default);

        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Equal("http://gateway.test:5000/api/recepciones/ordenes-pendientes?pagina=1&tamano=5", handler.Url);
        Assert.Equal("Bearer JWT_DEL_CAPATAZ", handler.Authorization);
        Assert.Equal("abc-123", handler.CorrelationId);
        Assert.Null(r.Error);
        Assert.Equal(7, r.Total);
    }

    [Fact]
    public async Task Traduce_la_orden_y_sus_materiales_e_ignora_campos_que_no_conoce()
    {
        var r = await Armar(new Handler(JsonLista)).ListarPendientesAsync(5, default);

        var orden = Assert.Single(r.Ordenes!);
        Assert.Equal(Guid.Parse(OrdenId), orden.OrdenCompraId);
        Assert.Equal(Guid.Parse(ObraId), orden.ObraId);
        Assert.Equal("Estructura", orden.Etapa);
        var item = Assert.Single(orden.Items);
        Assert.Equal("Cemento", item.Descripcion);
        Assert.Equal(80m, item.CantidadEsperada);
        Assert.Equal("BOLSA", item.UnidadMedida);
        Assert.Null(r.Total);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API()
    {
        var handler = new Handler(JsonLista);

        var r = await Armar(handler, new ContextoFalso { Jwt = null }).ListarPendientesAsync(5, default);

        Assert.Equal("sin_permiso", r.Error);
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sin_permiso")]
    [InlineData(HttpStatusCode.Forbidden, "sin_permiso")]
    [InlineData(HttpStatusCode.InternalServerError, "api_http_error")]
    public async Task Los_errores_http_se_traducen_a_codigos_estables(HttpStatusCode estado, string esperado)
    {
        var r = await Armar(new Handler("{}", estado: estado)).ListarPendientesAsync(5, default);

        Assert.Equal(esperado, r.Error);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_json_valido_es_respuesta_invalida()
    {
        var r = await Armar(new Handler("<html>")).ListarPendientesAsync(5, default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    [Fact]
    public async Task Una_respuesta_demasiado_grande_se_descarta()
    {
        var r = await Armar(new Handler(new string('x', RecepcionesGateway.MaxBytes + 1))).ListarPendientesAsync(5, default);

        Assert.Equal("respuesta_demasiado_grande", r.Error);
    }

    [Fact]
    public async Task Si_el_gateway_no_responde_es_api_no_disponible()
    {
        var r = await Armar(new Handler("", falla: new HttpRequestException("caído"))).ListarPendientesAsync(5, default);

        Assert.Equal("api_no_disponible", r.Error);
    }

    [Fact]
    public async Task Si_se_pasa_del_tiempo_es_api_timeout()
    {
        var r = await Armar(new Handler("", falla: new TaskCanceledException())).ListarPendientesAsync(5, default);

        Assert.Equal("api_timeout", r.Error);
    }
}
