using System.Net;
using System.Text;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

/// <summary>El cliente de lectura de las herramientas generadas (#151): solo GET, con el JWT del usuario y con errores estables.</summary>
public class ApiLecturaGatewayTests
{
    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private sealed class HandlerLectura : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responder;
        public int Llamadas { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Url { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }

        public HandlerLectura(HttpStatusCode estado, string cuerpo, string? total = null)
            : this(() =>
            {
                var r = new HttpResponseMessage(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
                if (total is not null)
                    r.Headers.Add("X-Total-Count", total);
                return r;
            })
        {
        }

        public HandlerLectura(Func<HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Llamadas++;
            Metodo = req.Method;
            Url = req.RequestUri!.AbsoluteUri;
            Authorization = req.Headers.Authorization?.ToString();
            CorrelationId = req.Headers.TryGetValues("X-Correlation-ID", out var v) ? v.FirstOrDefault() : null;
            return Task.FromResult(_responder());
        }
    }

    private static ApiLecturaGateway Armar(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso { Rol = "Comprador" });

    [Fact]
    public async Task Hace_un_GET_con_el_JWT_del_usuario_y_el_correlation_id()
    {
        var handler = new HandlerLectura(HttpStatusCode.OK, "[{\"x\":1}]", "7");
        var contexto = new ContextoFalso { Jwt = "JWT_DEL_USUARIO", CorrelationId = "abc-123" };

        var r = await Armar(handler, contexto).GetAsync("/api/usuarios/proveedores?tamano=10", default);

        Assert.True(r.Ok);
        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.Equal("http://gateway.test:5000/api/usuarios/proveedores?tamano=10", handler.Url);
        Assert.Equal("Bearer JWT_DEL_USUARIO", handler.Authorization);
        Assert.Equal("abc-123", handler.CorrelationId);
        Assert.Equal(7, r.Total);
        Assert.NotNull(r.Cuerpo);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API()
    {
        var handler = new HandlerLectura(HttpStatusCode.OK, "[]");

        var r = await Armar(handler, new ContextoFalso { Jwt = null }).GetAsync("/api/obras", default);

        Assert.Equal("sin_permiso", r.Error);
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData("http://otro.sitio/api/obras")]
    [InlineData("/otra-ruta")]
    [InlineData("/api/../admin")]
    [InlineData("/api//obras")]
    [InlineData("api/obras")]
    public async Task Una_ruta_que_no_es_de_la_API_se_rechaza_sin_llamar(string ruta)
    {
        var handler = new HandlerLectura(HttpStatusCode.OK, "[]");

        var r = await Armar(handler).GetAsync(ruta, default);

        Assert.Equal("ruta_invalida", r.Error);
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sin_permiso")]
    [InlineData(HttpStatusCode.Forbidden, "sin_permiso")]
    [InlineData(HttpStatusCode.NotFound, "no_encontrado")]
    [InlineData(HttpStatusCode.InternalServerError, "api_http_error")]
    [InlineData(HttpStatusCode.BadRequest, "api_http_error")]
    public async Task Los_errores_de_la_API_salen_como_codigos_estables(HttpStatusCode estado, string esperado)
    {
        var r = await Armar(new HandlerLectura(estado, "{}")).GetAsync("/api/obras", default);

        Assert.False(r.Ok);
        Assert.Equal(esperado, r.Error);
    }

    [Fact]
    public async Task Una_respuesta_mas_grande_que_el_tope_se_descarta()
    {
        var grande = "[\"" + new string('a', ApiLecturaGateway.MaxBytes) + "\"]";

        var r = await Armar(new HandlerLectura(HttpStatusCode.OK, grande)).GetAsync("/api/obras", default);

        Assert.Equal("respuesta_demasiado_grande", r.Error);
    }

    [Fact]
    public async Task Un_cuerpo_que_no_es_JSON_da_respuesta_invalida()
    {
        var r = await Armar(new HandlerLectura(HttpStatusCode.OK, "esto no es json")).GetAsync("/api/obras", default);

        Assert.Equal("respuesta_invalida", r.Error);
    }

    [Fact]
    public async Task Si_la_API_tarda_demasiado_da_api_timeout()
    {
        var handler = new HandlerLectura(() => throw new TaskCanceledException());

        var r = await Armar(handler).GetAsync("/api/obras", default);

        Assert.Equal("api_timeout", r.Error);
    }

    [Fact]
    public async Task Si_el_Gateway_no_responde_da_api_no_disponible()
    {
        var handler = new HandlerLectura(() => throw new HttpRequestException("caído"));

        var r = await Armar(handler).GetAsync("/api/obras", default);

        Assert.Equal("api_no_disponible", r.Error);
    }
}
