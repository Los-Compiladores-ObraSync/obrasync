using System.Net;
using System.Text;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>El lector de licitaciones y cotizaciones (#148): solo GET, siempre con el JWT del usuario y con errores estables.</summary>
public class LicitacionesGatewayTests
{
    private const string IdLic = "11111111-1111-1111-1111-111111111111";
    private const string IdCot = "22222222-2222-2222-2222-222222222222";
    private const string IdProv = "abcdef12-3333-3333-3333-333333333333";

    private const string JsonLicitaciones =
        "[{\"licitacionId\":\"" + IdLic + "\",\"requisicionId\":\"44444444-4444-4444-4444-444444444444\"," +
        "\"numeroRequisicion\":\"REQ-0007\",\"obraId\":\"55555555-5555-5555-5555-555555555555\",\"estado\":\"Abierta\"," +
        "\"fechaCreacion\":\"2026-11-10T14:30:00Z\",\"fechaLimiteCotizacion\":\"2026-11-20T02:59:00Z\"," +
        "\"items\":[{\"articuloId\":\"66666666-6666-6666-6666-666666666666\",\"descripcion\":\"Hierro 8 mm\",\"cantidad\":100.5,\"unidadMedida\":\"BARRA\"}]," +
        "\"yaCotizada\":null,\"campoNuevoQueNoConocemos\":1}]";

    private const string JsonCotizaciones =
        "[{\"cotizacionId\":\"" + IdCot + "\",\"licitacionId\":\"" + IdLic + "\",\"proveedorId\":\"" + IdProv + "\"," +
        "\"subtotalItems\":90000.5,\"flete\":5000,\"costoTotal\":95000.5,\"esMasBarata\":true,\"plazoEntregaDias\":3," +
        "\"fechaCreacion\":\"2026-11-12T10:00:00Z\",\"items\":[],\"puntajeTotal\":82.5,\"esGanadora\":true," +
        "\"proveedorNombre\":null,\"calificacionProveedor\":4,\"desglosePuntaje\":{\"precio\":1,\"flete\":1,\"plazo\":1,\"calificacion\":1}}]";

    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private static LicitacionesGateway Armar(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso { Rol = "Comprador" });

    /// <summary>Gateway falso que devuelve el cuerpo pedido, anota el pedido y puede mandar X-Total-Count.</summary>
    private sealed class HandlerConTotal : HttpMessageHandler
    {
        private readonly string _cuerpo;
        private readonly string? _total;
        public string? Url { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }

        public HandlerConTotal(string cuerpo, string? total = null)
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
    public async Task Listar_hace_un_GET_con_el_JWT_el_correlation_id_el_estado_y_la_paginacion()
    {
        var handler = new HandlerConTotal(JsonLicitaciones, "12");
        var contexto = new ContextoFalso { Jwt = "JWT_DEL_COMPRADOR", CorrelationId = "abc-123", Rol = "Comprador" };

        var r = await Armar(handler, contexto).ListarAsync("Abierta", 5, default);

        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Equal("http://gateway.test:5000/api/licitaciones?pagina=1&tamano=5&estado=Abierta", handler.Url);
        Assert.Equal("Bearer JWT_DEL_COMPRADOR", handler.Authorization);
        Assert.Equal("abc-123", handler.CorrelationId);
        Assert.Equal(12, r.Total);
    }

    [Fact]
    public async Task Listar_sin_estado_no_manda_el_filtro_y_convierte_la_respuesta()
    {
        var handler = new HandlerConTotal(JsonLicitaciones);

        var r = await Armar(handler).ListarAsync(null, 5, default);

        Assert.DoesNotContain("estado=", handler.Url);
        Assert.Null(r.Error);
        var l = Assert.Single(r.Licitaciones!);
        Assert.Equal(Guid.Parse(IdLic), l.Id);
        Assert.Equal("REQ-0007", l.NumeroRequisicion);
        Assert.Equal("Abierta", l.Estado);
        Assert.Equal(1, l.CantidadItems);
        Assert.Equal(new DateTime(2026, 11, 20, 2, 59, 0, DateTimeKind.Utc), l.FechaLimiteCotizacion.ToUniversalTime());
    }

    [Fact]
    public async Task Ver_pide_la_licitacion_por_id_y_trae_los_materiales()
    {
        var handler = new HandlerConTotal(JsonLicitaciones.Trim('[', ']'));

        var r = await Armar(handler).ObtenerAsync(Guid.Parse(IdLic), default);

        Assert.Equal($"http://gateway.test:5000/api/licitaciones/{IdLic}", handler.Url);
        Assert.Null(r.Error);
        var item = Assert.Single(r.Licitacion!.Items);
        Assert.Equal("Hierro 8 mm", item.Descripcion);
        Assert.Equal(100.5m, item.Cantidad);
        Assert.Equal("BARRA", item.UnidadMedida);
    }

    [Fact]
    public async Task Las_cotizaciones_se_piden_por_licitacion_y_se_copian_las_cifras_que_calcula_el_backend()
    {
        var handler = new HandlerConTotal(JsonCotizaciones, "1");

        var r = await Armar(handler).ListarCotizacionesAsync(Guid.Parse(IdLic), 10, default);

        Assert.Equal($"http://gateway.test:5000/api/cotizaciones?licitacionId={IdLic}&pagina=1&tamano=10", handler.Url);
        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Null(r.Error);
        var c = Assert.Single(r.Cotizaciones!);
        Assert.Equal(Guid.Parse(IdCot), c.Id);
        Assert.Equal(Guid.Parse(IdProv), c.ProveedorId);
        Assert.Null(c.ProveedorNombre);
        Assert.Equal(90000.5m, c.SubtotalItems);
        Assert.Equal(5000m, c.Flete);
        Assert.Equal(95000.5m, c.CostoTotal);
        Assert.Equal(3, c.PlazoEntregaDias);
        Assert.Equal(82.5m, c.PuntajeTotal);
        Assert.True(c.EsGanadora);
        Assert.True(c.EsMasBarata);
        Assert.Equal(4m, c.Calificacion);
        Assert.Equal(1, r.Total);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sin_permiso")]
    [InlineData(HttpStatusCode.Forbidden, "sin_permiso")]
    [InlineData(HttpStatusCode.NotFound, "licitacion_no_encontrada")]
    [InlineData(HttpStatusCode.InternalServerError, "api_http_error")]
    [InlineData(HttpStatusCode.BadRequest, "api_http_error")]
    public async Task Los_errores_de_la_API_se_traducen_a_codigos_estables(HttpStatusCode estado, string esperado)
    {
        var r = await Armar(new HandlerCatalogo((int)estado, "{}")).ListarCotizacionesAsync(Guid.Parse(IdLic), 10, default);

        Assert.Equal(esperado, r.Error);
        Assert.Null(r.Cotizaciones);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API()
    {
        var handler = new HandlerConTotal(JsonLicitaciones);

        var r = await Armar(handler, new ContextoFalso { Jwt = null, Rol = "Comprador" }).ListarAsync(null, 5, default);

        Assert.Equal("sin_permiso", r.Error);
        Assert.Null(handler.Url);
    }

    [Fact]
    public async Task Una_respuesta_que_no_es_json_da_respuesta_invalida()
    {
        var r = await Armar(new HandlerCatalogo(200, "esto no es json")).ListarAsync(null, 5, default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    [Fact]
    public async Task Una_respuesta_demasiado_grande_se_corta()
    {
        var grande = "[" + new string(' ', LicitacionesGateway.MaxBytes + 10) + "]";

        var r = await Armar(new HandlerCatalogo(200, grande)).ListarCotizacionesAsync(Guid.Parse(IdLic), 10, default);

        Assert.Equal("respuesta_demasiado_grande", r.Error);
    }

    [Fact]
    public async Task Si_el_Gateway_no_responde_da_api_no_disponible()
    {
        var r = await Armar(new HandlerCatalogo(200, "{}", lanzaError: true)).ListarAsync(null, 5, default);

        Assert.Equal("api_no_disponible", r.Error);
    }
}
