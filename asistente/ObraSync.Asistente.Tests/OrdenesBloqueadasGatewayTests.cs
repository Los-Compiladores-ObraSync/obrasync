using System.Net;
using System.Text;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

public class OrdenesBloqueadasGatewayTests
{
    private static readonly Guid ObraId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrdenId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string JsonOrden = "[{\"ordenCompraId\":\"33333333-3333-3333-3333-333333333333\",\"obraId\":\"22222222-2222-2222-2222-222222222222\",\"etapa\":\"Estructura\",\"montoOrdenCompra\":120.5,\"montoAsignadoPartida\":100,\"montoComprometidoActual\":25,\"montoProyectado\":145.5,\"exceso\":45.5,\"motivo\":\"Excede presupuesto\",\"partidaId\":null,\"fechaBloqueo\":\"2026-11-16T10:30:00Z\"}]";

    private sealed class ContextoPrueba : IContextoDePeticion
    {
        public string? Jwt { get; init; } = "JWT_COMPRADOR";
        public string? Rol => "Comprador";
        public string CorrelationId => "corr-210";
    }

    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
        public Uri? Url { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }
        public int Llamadas { get; private set; }

        public HandlerFalso(HttpResponseMessage respuesta) : this((_, _) => Task.FromResult(respuesta)) { }
        public HandlerFalso(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Llamadas++;
            Url = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            CorrelationId = request.Headers.TryGetValues("X-Correlation-ID", out var ids) ? ids.FirstOrDefault() : null;
            return _responder(request, ct);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int _position;
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, int? total = 1)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (total.HasValue)
            response.Headers.Add("X-Total-Count", total.Value.ToString());
        return response;
    }

    private static OrdenesBloqueadasGateway Crear(HttpMessageHandler handler, string? jwt = "JWT_COMPRADOR") =>
        new(new HttpClient(handler), new ConfiguracionApi("http://gateway.test:5000/"), new ContextoPrueba { Jwt = jwt });

    [Fact]
    public async Task Usa_la_URL_real_y_solo_parametros_de_paginacion_sin_etapa()
    {
        var handler = new HandlerFalso(Json(HttpStatusCode.OK, "[]", 0));
        await Crear(handler).ListarAsync(ObraId, 2, 50, default);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("http://gateway.test:5000/api/recepciones/ordenes-bloqueadas", handler.Url!.GetLeftPart(UriPartial.Path));
        Assert.Contains($"obraId={ObraId:D}", handler.Url.Query);
        Assert.Contains("pagina=2", handler.Url.Query);
        Assert.Contains("tamano=50", handler.Url.Query);
        Assert.DoesNotContain("etapa", handler.Url.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer JWT_COMPRADOR", handler.Authorization);
        Assert.Equal("corr-210", handler.CorrelationId);
    }

    [Fact]
    public async Task Deserializa_todos_los_campos_reales_y_respeta_nullable_y_utc()
    {
        var json = JsonOrden.Replace("\"partidaId\":null", "\"partidaId\":\"44444444-4444-4444-4444-444444444444\"");
        var handler = new HandlerFalso(Json(HttpStatusCode.OK, json, 3));
        var resultado = await Crear(handler).ListarAsync(ObraId, 1, 50, default);

        Assert.Null(resultado.Error);
        Assert.Equal(3, resultado.Total);
        var orden = Assert.Single(resultado.Ordenes!);
        Assert.Equal(OrdenId, orden.OrdenCompraId);
        Assert.Equal(ObraId, orden.ObraId);
        Assert.Equal("Estructura", orden.Etapa);
        Assert.Equal(120.5m, orden.MontoOrdenCompra);
        Assert.Equal(100m, orden.MontoAsignadoPartida);
        Assert.Equal(25m, orden.MontoComprometidoActual);
        Assert.Equal(145.5m, orden.MontoProyectado);
        Assert.Equal(45.5m, orden.Exceso);
        Assert.Equal("Excede presupuesto", orden.Motivo);
        Assert.Equal(Guid.Parse("44444444-4444-4444-4444-444444444444"), orden.PartidaId);
        Assert.Equal(DateTimeKind.Utc, orden.FechaBloqueo.Kind);
        Assert.Equal(new DateTime(2026, 11, 16, 10, 30, 0, DateTimeKind.Utc), orden.FechaBloqueo);
    }

    [Fact]
    public async Task Deserializa_etapa_y_partida_nulas_y_lista_vacia()
    {
        var json = JsonOrden.Replace("\"etapa\":\"Estructura\"", "\"etapa\":null");
        var handler = new HandlerFalso(Json(HttpStatusCode.OK, json, 1));
        var resultado = await Crear(handler).ListarAsync(ObraId, 1, 50, default);

        Assert.Null(resultado.Error);
        var orden = Assert.Single(resultado.Ordenes!);
        Assert.Null(orden.Etapa);
        Assert.Null(orden.PartidaId);

        var vacio = await Crear(new HandlerFalso(Json(HttpStatusCode.OK, "[]", 0)))
            .ListarAsync(ObraId, 1, 50, default);
        Assert.Null(vacio.Error);
        Assert.Empty(vacio.Ordenes!);
        Assert.Equal(0, vacio.Total);
    }

    [Fact]
    public async Task JWT_ausente_no_llama_al_Gateway()
    {
        var handler = new HandlerFalso(Json(HttpStatusCode.OK, "[]", 0));
        var resultado = await Crear(handler, null).ListarAsync(ObraId, 1, 50, default);
        Assert.Equal("sin_permiso", resultado.Error);
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sin_permiso")]
    [InlineData(HttpStatusCode.Forbidden, "sin_permiso")]
    [InlineData(HttpStatusCode.InternalServerError, "api_http_error")]
    [InlineData(HttpStatusCode.BadGateway, "api_http_error")]
    public async Task Traduce_errores_HTTP(HttpStatusCode status, string error)
    {
        var resultado = await Crear(new HandlerFalso(Json(status, "{}"))).ListarAsync(ObraId, 1, 50, default);
        Assert.Equal(error, resultado.Error);
    }

    [Fact]
    public async Task Traduce_error_de_conexion()
    {
        var handler = new HandlerFalso((_, _) => throw new HttpRequestException("offline"));
        var resultado = await Crear(handler).ListarAsync(ObraId, 1, 50, default);
        Assert.Equal("api_no_disponible", resultado.Error);
    }

    [Fact]
    public async Task Traduce_JSON_invalido_o_total_ausente()
    {
        var jsonInvalido = await Crear(new HandlerFalso(Json(HttpStatusCode.OK, "no-json")))
            .ListarAsync(ObraId, 1, 50, default);
        var jsonNulo = await Crear(new HandlerFalso(Json(HttpStatusCode.OK, "null")))
            .ListarAsync(ObraId, 1, 50, default);
        var sinTotal = await Crear(new HandlerFalso(Json(HttpStatusCode.OK, "[]", null)))
            .ListarAsync(ObraId, 1, 50, default);
        Assert.Equal("respuesta_invalida", jsonInvalido.Error);
        Assert.Equal("respuesta_invalida", jsonNulo.Error);
        Assert.Equal("respuesta_invalida", sinTotal.Error);
    }

    [Fact]
    public async Task Timeout_interno_y_cancelacion_del_llamador_se_distinguen()
    {
        var timeoutHandler = new HandlerFalso(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "[]", 0);
        });
        var timeoutGateway = new OrdenesBloqueadasGateway(
            new HttpClient(timeoutHandler), new ConfiguracionApi("http://gateway.test"), new ContextoPrueba());
        Assert.Equal("api_timeout", (await timeoutGateway.ListarAsync(ObraId, 1, 50, default)).Error);

        using var cancelacion = new CancellationTokenSource();
        var cancelHandler = new HandlerFalso(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK, "[]", 0);
        });
        var consulta = Crear(cancelHandler).ListarAsync(ObraId, 1, 50, cancelacion.Token);
        cancelacion.CancelAfter(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consulta);
    }

    [Theory]
    [InlineData(OrdenesBloqueadasGateway.MaxBytes, true, true)]
    [InlineData(OrdenesBloqueadasGateway.MaxBytes, false, true)]
    [InlineData(OrdenesBloqueadasGateway.MaxBytes + 1, true, false)]
    [InlineData(OrdenesBloqueadasGateway.MaxBytes + 1, false, false)]
    public async Task Limita_lectura_a_64_KiB_mas_un_byte_de_control(int size, bool contentLength, bool accepted)
    {
        var body = Encoding.UTF8.GetBytes("[]" + new string(' ', size - 2));
        var stream = new NonSeekableStream(body);
        var content = new StreamContent(stream);
        if (contentLength)
            content.Headers.ContentLength = size;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        response.Headers.Add("X-Total-Count", "0");
        var result = await Crear(new HandlerFalso(response)).ListarAsync(ObraId, 1, 50, default);

        if (accepted)
        {
            Assert.Null(result.Error);
            Assert.Equal(OrdenesBloqueadasGateway.MaxBytes, stream.BytesRead);
        }
        else
        {
            Assert.Equal("respuesta_demasiado_grande", result.Error);
            Assert.Equal(contentLength ? 0 : OrdenesBloqueadasGateway.MaxBytes + 1, stream.BytesRead);
        }
    }
}
