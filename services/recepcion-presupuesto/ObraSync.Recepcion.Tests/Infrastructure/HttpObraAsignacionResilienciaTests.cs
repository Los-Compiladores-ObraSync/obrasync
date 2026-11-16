using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Infrastructure.Services;
using Polly.CircuitBreaker;
using Xunit;

namespace ObraSync.Recepcion.Tests.Infrastructure;

/// <summary>
/// Pruebas unitarias de resiliencia con Polly v8 (#103):
/// - Reintentos con retroceso exponencial ante fallas transitorias (5xx, timeouts, caídas de conexión).
/// - No reintento ante errores de autorización (401, 403) ni errores de cliente (400, 404).
/// - Circuit breaker que se abre ante fallas consecutivas y corta de inmediato sin salir a la red.
/// - Recuperación tras la pausa del circuit breaker.
/// </summary>
public class HttpObraAsignacionResilienciaTests
{
    private const string BaseUrl = "http://usuarios-obras-api:8080";

    private sealed class ServicioMock : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _respuestas = new();
        public int Llamadas { get; private set; }

        public ServicioMock Responde(HttpStatusCode estado, object? body = null)
        {
            _respuestas.Enqueue(() =>
            {
                var resp = new HttpResponseMessage(estado);
                if (body != null)
                {
                    resp.Content = JsonContent.Create(body);
                }
                return resp;
            });
            return this;
        }

        public ServicioMock FallaCon(Func<Exception> excepcionFactory)
        {
            _respuestas.Enqueue(() => throw excepcionFactory());
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            if (_respuestas.Count == 0)
            {
                throw new InvalidOperationException("No hay más respuestas programadas en ServicioMock.");
            }

            var func = _respuestas.Count > 1 ? _respuestas.Dequeue() : _respuestas.Peek();
            return Task.FromResult(func());
        }
    }

    private static (HttpObraAsignacionValidator validador, ServicioMock servicio) CrearValidador(
        ResilienciaHttpOptions? opciones = null,
        Polly.ResiliencePipeline<HttpResponseMessage>? pipelineCompartido = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UsuariosObras:BaseUrl"] = BaseUrl
            })
            .Build();

        var servicio = new ServicioMock();
        var client = new HttpClient(servicio);

        var pipeline = pipelineCompartido ?? PipelineResilienciaHttp.Crear(opciones ?? new ResilienciaHttpOptions
        {
            EsperaBaseMs = 1,
            MaxReintentos = 3
        });

        var validador = new HttpObraAsignacionValidator(config, client, pipeline: pipeline);
        return (validador, servicio);
    }

    [Fact]
    public async Task FalloTransitorio503_SeReintentaYRecuperaEnSegundoIntento()
    {
        var obraId = Guid.NewGuid();
        var (validador, servicio) = CrearValidador();

        servicio
            .Responde(HttpStatusCode.ServiceUnavailable)
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } });

        var asignada = await validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1");

        Assert.True(asignada);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task FalloTransitorio500_SeReintentaYRecuperaEnSegundoIntento()
    {
        var obraId = Guid.NewGuid();
        var (validador, servicio) = CrearValidador();

        servicio
            .Responde(HttpStatusCode.InternalServerError)
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } });

        var asignada = await validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1");

        Assert.True(asignada);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task FalloRedHttpRequestException_SeReintentaYRecupera()
    {
        var obraId = Guid.NewGuid();
        var (validador, servicio) = CrearValidador();

        servicio
            .FallaCon(() => new HttpRequestException("Connection reset by peer"))
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } });

        var asignada = await validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1");

        Assert.True(asignada);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task FalloTimeoutTaskCanceledException_SeReintentaYRecupera()
    {
        var obraId = Guid.NewGuid();
        var (validador, servicio) = CrearValidador();

        servicio
            .FallaCon(() => new TaskCanceledException("HttpClient timeout"))
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } });

        var asignada = await validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1");

        Assert.True(asignada);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task FallaPersistente_SuperaMaxReintentos_LanzaObraAsignacionNoVerificable()
    {
        var (validador, servicio) = CrearValidador(new ResilienciaHttpOptions
        {
            MaxReintentos = 2,
            EsperaBaseMs = 1,
            MinimoPedidos = 10
        });

        servicio.Responde(HttpStatusCode.ServiceUnavailable);

        var ex = await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));

        Assert.Contains("Usuarios y Obras", ex.Message);
        // 1 intento original + 2 reintentos = 3 llamadas en total
        Assert.Equal(3, servicio.Llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task ErrorAutorizacion_NoSeReintenta_RealizaSoloUnIntentoYRetornaFalse(HttpStatusCode codigo)
    {
        var (validador, servicio) = CrearValidador();

        servicio.Responde(codigo);

        var asignada = await validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token-invalido", "corr-1");

        Assert.False(asignada);
        // No debe reintentarse nunca una falla de autorización
        Assert.Equal(1, servicio.Llamadas);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ErrorCliente4xx_NoSeReintenta_RealizaSoloUnIntentoYLanzaExcepcion(HttpStatusCode codigo)
    {
        var (validador, servicio) = CrearValidador();

        servicio.Responde(codigo);

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));

        // No debe reintentarse
        Assert.Equal(1, servicio.Llamadas);
    }

    [Fact]
    public async Task HostNoEncontrado_SocketExceptionHostNotFound_NoSeReintenta()
    {
        var (validador, servicio) = CrearValidador();

        servicio.FallaCon(() => new HttpRequestException(
            "Name or service not known",
            new SocketException((int)SocketError.HostNotFound)));

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));

        // DNS inexistente no debe reintentarse
        Assert.Equal(1, servicio.Llamadas);
    }

    [Fact]
    public async Task CircuitBreaker_SeAbreTrasSuperarUmbral_YCortaLlamadasPosterioresSinSalirALaRed()
    {
        var opciones = new ResilienciaHttpOptions
        {
            MaxReintentos = 0,
            MinimoPedidos = 2,
            RatioFallas = 0.5,
            VentanaSegundos = 30,
            PausaSegundos = 15
        };

        var pipelineCompartido = PipelineResilienciaHttp.Crear(opciones);
        var (validador, servicio) = CrearValidador(opciones, pipelineCompartido);

        servicio.Responde(HttpStatusCode.ServiceUnavailable);

        // Primer pedido falla (1/1 fallas)
        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));
        Assert.Equal(1, servicio.Llamadas);

        // Segundo pedido falla (2/2 fallas -> supera MinimoPedidos y RatioFallas >= 0.5 -> abre breaker)
        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-2"));
        Assert.Equal(2, servicio.Llamadas);

        // Tercer pedido: el circuit breaker ya está abierto.
        // Debe fallar de inmediato con ObraAsignacionNoVerificableException e inner BrokenCircuitException.
        // El servicio de red NO debe recibir la llamada (Llamadas se mantiene en 2).
        var ex = await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-3"));

        Assert.IsType<BrokenCircuitException>(ex.InnerException);
        Assert.Contains("circuit breaker abierto", ex.Message);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task CircuitBreaker_TrasPausa_PasaASemiAbiertoYSeCierraConExito()
    {
        var opciones = new ResilienciaHttpOptions
        {
            MaxReintentos = 0,
            MinimoPedidos = 2,
            RatioFallas = 0.5,
            VentanaSegundos = 30,
            PausaSegundos = 1 // 1 segundo de pausa para la prueba
        };

        var pipelineCompartido = PipelineResilienciaHttp.Crear(opciones);
        var (validador, servicio) = CrearValidador(opciones, pipelineCompartido);

        var obraId = Guid.NewGuid();
        servicio
            .Responde(HttpStatusCode.ServiceUnavailable) // Llamada 1
            .Responde(HttpStatusCode.ServiceUnavailable) // Llamada 2 (abre breaker)
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } }); // Llamada 4 (recuperación)

        // Falla 1
        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1"));

        // Falla 2 (Abre circuit breaker)
        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-2"));

        Assert.Equal(2, servicio.Llamadas);

        // Llamada con breaker abierto: se corta de inmediato sin llamar a la red
        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-3"));
        Assert.Equal(2, servicio.Llamadas);

        // Esperar que transcurra PausaSegundos (1 segundo) para que pase a Half-Open
        await Task.Delay(1100);

        // Llamada de prueba en Half-Open: el servicio ahora responde 200 OK -> breaker vuelve a Closed
        var asignada = await validador.EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-4");

        Assert.True(asignada);
        Assert.Equal(3, servicio.Llamadas);
    }
}
