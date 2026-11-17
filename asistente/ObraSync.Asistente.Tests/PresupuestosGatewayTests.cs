using System.Net;
using System.Text;
using ObraSync.Asistente.Application;
using ObraSync.Asistente.Infrastructure;

namespace ObraSync.Asistente.Tests;

public class PresupuestosGatewayTests
{
    private static readonly Guid ObraId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PartidaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string JsonLista = "[{\"id\":\"11111111-1111-1111-1111-111111111111\",\"obraId\":\"22222222-2222-2222-2222-222222222222\",\"etapa\":\"Estructura\",\"codigoPartida\":\"EST-01\",\"descripcion\":\"Hormigón\",\"montoAsignado\":100000,\"montoComprometido\":25000,\"montoEjecutado\":10000,\"montoDisponible\":75000,\"moneda\":\"ARS\"}]";
    private static readonly ConfiguracionApi Api = new("http://gateway.test:5000/");

    private static PresupuestosGateway Armar(HttpMessageHandler handler, ContextoFalso? contexto = null) =>
        new(new HttpClient(handler), Api, contexto ?? new ContextoFalso());

    private sealed class HandlerFalso : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public Uri? Url { get; private set; }
        public HttpMethod? Metodo { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }
        public int Llamadas { get; private set; }

        public HandlerFalso(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        public HandlerFalso(HttpResponseMessage respuesta) : this((_, _) => Task.FromResult(respuesta)) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Llamadas++;
            Url = request.RequestUri;
            Metodo = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            CorrelationId = request.Headers.TryGetValues("X-Correlation-ID", out var valores)
                ? valores.FirstOrDefault()
                : null;
            return _responder(request, ct);
        }
    }

    private sealed class FlujoNaoSeekable(byte[] datos) : Stream
    {
        private int _posicion;
        public int BytesLeidos { get; private set; }
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
            var cantidad = Math.Min(buffer.Length, datos.Length - _posicion);
            datos.AsMemory(_posicion, cantidad).CopyTo(buffer);
            _posicion += cantidad;
            BytesLeidos += cantidad;
            return ValueTask.FromResult(cantidad);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage StreamResponse(byte[] body, bool withContentLength, FlujoNaoSeekable stream)
    {
        var content = new StreamContent(stream);
        if (withContentLength)
            content.Headers.ContentLength = body.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public async Task Listar_hace_GET_con_obra_pagina_uno_tamano_cincuenta_etapa_y_credenciales_del_usuario()
    {
        var handler = new HandlerFalso(JsonResponse(HttpStatusCode.OK, "[]"));
        var contexto = new ContextoFalso
        {
            Jwt = "JWT_DEL_COMPRADOR",
            CorrelationId = "corr-presupuesto-101",
            Rol = "Comprador"
        };

        await Armar(handler, contexto).ListarPartidasAsync(ObraId, "Obra Gruesa", default);

        Assert.Equal(HttpMethod.Get, handler.Metodo);
        Assert.NotNull(handler.Url);
        var query = Uri.UnescapeDataString(handler.Url!.Query);
        Assert.Contains($"obraId={ObraId:D}", query);
        Assert.Contains("pagina=1", query);
        Assert.Contains("tamano=50", query);
        Assert.Contains("etapa=Obra Gruesa", query);
        Assert.Equal("http://gateway.test:5000/api/presupuestos/partidas", handler.Url.GetLeftPart(UriPartial.Path));
        Assert.Equal("Bearer JWT_DEL_COMPRADOR", handler.Authorization);
        Assert.Equal("corr-presupuesto-101", handler.CorrelationId);
    }

    [Fact]
    public async Task Listar_omite_etapa_vacia()
    {
        var handler = new HandlerFalso(JsonResponse(HttpStatusCode.OK, "[]"));

        await Armar(handler).ListarPartidasAsync(ObraId, "  ", default);

        Assert.DoesNotContain("etapa", handler.Url!.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Listar_deserializa_la_lista_y_lee_X_Total_Count()
    {
        var response = JsonResponse(HttpStatusCode.OK, JsonLista);
        response.Headers.Add("X-Total-Count", "7");
        var resultado = await Armar(new HandlerFalso(response)).ListarPartidasAsync(ObraId, null, default);

        Assert.Null(resultado.Error);
        Assert.Equal(7, resultado.Total);
        var partida = Assert.Single(resultado.Partidas!);
        Assert.Equal(PartidaId, partida.Id);
        Assert.Equal(ObraId, partida.ObraId);
        Assert.Equal("Estructura", partida.Etapa);
        Assert.Equal("EST-01", partida.CodigoPartida);
        Assert.Equal("Hormigón", partida.Descripcion);
        Assert.Equal(100000m, partida.MontoAsignado);
        Assert.Equal(25000m, partida.MontoComprometido);
        Assert.Equal(10000m, partida.MontoEjecutado);
        Assert.Equal(75000m, partida.MontoDisponible);
        Assert.Equal("ARS", partida.Moneda);
    }

    [Fact]
    public async Task Lista_vacia_es_una_respuesta_valida()
    {
        var resultado = await Armar(new HandlerFalso(JsonResponse(HttpStatusCode.OK, "[]")))
            .ListarPartidasAsync(ObraId, null, default);

        Assert.Null(resultado.Error);
        Assert.Empty(resultado.Partidas!);
    }

    [Fact]
    public async Task Sin_JWT_no_llama_a_la_API()
    {
        var handler = new HandlerFalso(JsonResponse(HttpStatusCode.OK, "[]"));

        var resultado = await Armar(handler, new ContextoFalso { Jwt = null })
            .ListarPartidasAsync(ObraId, null, default);

        Assert.Equal("sin_permiso", resultado.Error);
        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sin_permiso")]
    [InlineData(HttpStatusCode.Forbidden, "sin_permiso")]
    [InlineData(HttpStatusCode.InternalServerError, "api_http_error")]
    [InlineData(HttpStatusCode.NotFound, "api_http_error")]
    public async Task Los_estados_HTTP_se_traducen_a_errores_estables(HttpStatusCode status, string error)
    {
        var resultado = await Armar(new HandlerFalso(JsonResponse(status, "{}")))
            .ListarPartidasAsync(ObraId, null, default);

        Assert.Equal(error, resultado.Error);
    }

    [Fact]
    public async Task Error_de_conexion_se_traduce_a_api_no_disponible()
    {
        var handler = new HandlerFalso((_, _) => throw new HttpRequestException("Gateway fuera de línea"));

        var resultado = await Armar(handler).ListarPartidasAsync(ObraId, null, default);

        Assert.Equal("api_no_disponible", resultado.Error);
    }

    [Fact]
    public async Task JSON_invalido_se_traduce_a_respuesta_invalida()
    {
        var resultado = await Armar(new HandlerFalso(JsonResponse(HttpStatusCode.OK, "not-json")))
            .ListarPartidasAsync(ObraId, null, default);

        Assert.Equal("respuesta_invalida", resultado.Error);
    }

    [Fact]
    public async Task Timeout_interno_se_traduce_a_api_timeout()
    {
        var handler = new HandlerFalso(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        var resultado = await Armar(handler).ListarPartidasAsync(ObraId, null, default);

        Assert.Equal("api_timeout", resultado.Error);
    }

    [Fact]
    public async Task Cancelacion_del_llamador_se_propaga()
    {
        using var cancelacion = new CancellationTokenSource();
        var handler = new HandlerFalso(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        var consulta = Armar(handler).ListarPartidasAsync(ObraId, null, cancelacion.Token);
        cancelacion.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consulta);
    }

    [Theory]
    [InlineData(PresupuestosGateway.MaxBytes, true, true)]
    [InlineData(PresupuestosGateway.MaxBytes, false, true)]
    [InlineData(PresupuestosGateway.MaxBytes + 1, true, false)]
    [InlineData(PresupuestosGateway.MaxBytes + 1, false, false)]
    public async Task El_limite_de_64_KiB_se_aplica_con_y_sin_Content_Length(
        int tamano,
        bool withContentLength,
        bool debeAceptar)
    {
        var body = Encoding.UTF8.GetBytes("[]" + new string(' ', tamano - 2));
        var stream = new FlujoNaoSeekable(body);
        var response = StreamResponse(body, withContentLength, stream);
        var resultado = await Armar(new HandlerFalso(response)).ListarPartidasAsync(ObraId, null, default);

        if (debeAceptar)
        {
            Assert.Null(resultado.Error);
            Assert.Empty(resultado.Partidas!);
            Assert.Equal(PresupuestosGateway.MaxBytes, stream.BytesLeidos);
        }
        else
        {
            Assert.Equal("respuesta_demasiado_grande", resultado.Error);
            Assert.Equal(withContentLength ? 0 : PresupuestosGateway.MaxBytes + 1, stream.BytesLeidos);
        }
    }
}
