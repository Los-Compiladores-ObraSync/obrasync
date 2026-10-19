using System.Net;
using Polly.CircuitBreaker;
using ObraSync.Gateway;

namespace ObraSync.Gateway.Tests;

public class ResilienciaTests
{
    // Servicio de mentira: devuelve, en orden, lo que le cargamos (una respuesta o una falla) y cuenta cuántas veces lo llamaron.
    private sealed class ServicioQueFalla : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _respuestas = new();
        public int Llamadas { get; private set; }

        public ServicioQueFalla Responde(HttpStatusCode estado)
        {
            _respuestas.Enqueue(() => new HttpResponseMessage(estado));
            return this;
        }

        public ServicioQueFalla NoSePuedeConectar()
        {
            _respuestas.Enqueue(() => throw new HttpRequestException("no se pudo conectar"));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            // Si se acaban las respuestas cargadas, se repite la última.
            var siguiente = _respuestas.Count > 1 ? _respuestas.Dequeue() : _respuestas.Peek();
            return Task.FromResult(siguiente());
        }
    }

    // Esperas de 1 ms para que los tests no tarden.
    private static ResilienciaOptions Opciones(int reintentos = 3, int minimoPedidos = 5) => new()
    {
        MaxReintentos = reintentos,
        EsperaBaseMs = 1,
        MinimoPedidos = minimoPedidos,
        RatioFallas = 0.5,
        VentanaSegundos = 10,
        PausaSegundos = 30
    };

    private static HttpMessageInvoker Cliente(ServicioQueFalla servicio, ResilienciaOptions opciones)
        => new(new ResilienciaHandler(PipelineDeResiliencia.Crear(opciones), servicio));

    private static HttpRequestMessage Pedido(HttpMethod metodo) => new(metodo, "http://servicio/prueba");

    [Fact]
    public async Task Un_GET_que_falla_se_reintenta_hasta_que_anda()
    {
        var servicio = new ServicioQueFalla().NoSePuedeConectar().NoSePuedeConectar().Responde(HttpStatusCode.OK);
        var cliente = Cliente(servicio, Opciones());

        var respuesta = await cliente.SendAsync(Pedido(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(3, servicio.Llamadas);
    }

    [Fact]
    public async Task Un_GET_con_503_se_reintenta()
    {
        var servicio = new ServicioQueFalla().Responde(HttpStatusCode.ServiceUnavailable).Responde(HttpStatusCode.OK);
        var cliente = Cliente(servicio, Opciones());

        var respuesta = await cliente.SendAsync(Pedido(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task Un_GET_deja_de_reintentar_al_llegar_al_maximo()
    {
        var servicio = new ServicioQueFalla().NoSePuedeConectar();
        var cliente = Cliente(servicio, Opciones(reintentos: 2));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => cliente.SendAsync(Pedido(HttpMethod.Get), CancellationToken.None));

        // 1 intento inicial + 2 reintentos
        Assert.Equal(3, servicio.Llamadas);
    }

    [Fact]
    public async Task Un_POST_que_falla_NO_se_reintenta()
    {
        var servicio = new ServicioQueFalla().NoSePuedeConectar();
        var cliente = Cliente(servicio, Opciones());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => cliente.SendAsync(Pedido(HttpMethod.Post), CancellationToken.None));

        Assert.Equal(1, servicio.Llamadas);
    }

    [Fact]
    public async Task Un_500_no_se_reintenta_y_pasa_igual()
    {
        var servicio = new ServicioQueFalla().Responde(HttpStatusCode.InternalServerError);
        var cliente = Cliente(servicio, Opciones());

        var respuesta = await cliente.SendAsync(Pedido(HttpMethod.Get), CancellationToken.None);

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Equal(1, servicio.Llamadas);
    }

    [Fact]
    public async Task El_circuit_breaker_se_abre_y_corta_sin_llamar_al_servicio()
    {
        var servicio = new ServicioQueFalla().NoSePuedeConectar();
        var cliente = Cliente(servicio, Opciones(minimoPedidos: 2));

        // Dos POST fallidos (sin reintentos) alcanzan el mínimo de pedidos con 100% de fallas.
        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => cliente.SendAsync(Pedido(HttpMethod.Post), CancellationToken.None));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(
            () => cliente.SendAsync(Pedido(HttpMethod.Post), CancellationToken.None));

        // El tercer pedido no llegó al servicio.
        Assert.Equal(2, servicio.Llamadas);
    }

    [Fact]
    public async Task Cada_pipeline_tiene_su_propio_breaker()
    {
        var caido = new ServicioQueFalla().NoSePuedeConectar();
        var sano = new ServicioQueFalla().Responde(HttpStatusCode.OK);
        var clienteCaido = Cliente(caido, Opciones(minimoPedidos: 2));
        var clienteSano = Cliente(sano, Opciones(minimoPedidos: 2));

        for (var i = 0; i < 2; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(
                () => clienteCaido.SendAsync(Pedido(HttpMethod.Post), CancellationToken.None));
        }

        var respuesta = await clienteSano.SendAsync(Pedido(HttpMethod.Post), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
