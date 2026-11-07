using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Yarp.ReverseProxy.Forwarder;

namespace ObraSync.Gateway.Tests;

/// <summary>
/// Tarjeta #124. A diferencia de ResilienciaTests (que prueba Polly suelto), acá pasa TODO el camino:
/// cliente -> Gateway real (CorrelationId, YARP, Polly, ProblemDetails) -> servicio de mentira que se "cae".
/// Solo se reemplaza la conexión de red: el pipeline de Polly es el mismo que usa el Gateway en producción.
/// </summary>
public class ResilienciaDeExtremoAExtremoTests
{
    /// <summary>Servicio de mentira: cuenta cuántas veces lo llamaron y se puede "apagar" y "prender".</summary>
    private sealed class ServicioDeMentira : HttpMessageHandler
    {
        private int _llamadas;
        public int Llamadas => _llamadas;
        public volatile bool Caido;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _llamadas);
            if (Caido)
            {
                throw new HttpRequestException("no se pudo conectar");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>Arma, igual que ForwarderConResilienciaFactory, un pipeline (y un breaker) por servicio, pero sin salir a la red.</summary>
    private sealed class ClienteConPollyFactory : IForwarderHttpClientFactory
    {
        private readonly ResilienciaOptions _opciones;
        private readonly IReadOnlyDictionary<string, ServicioDeMentira> _servicios;
        private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _pipelines = new();

        public ClienteConPollyFactory(ResilienciaOptions opciones, IReadOnlyDictionary<string, ServicioDeMentira> servicios)
        {
            _opciones = opciones;
            _servicios = servicios;
        }

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
        {
            var pipeline = _pipelines.GetOrAdd(context.ClusterId, _ => PipelineDeResiliencia.Crear(_opciones));
            return new HttpMessageInvoker(new ResilienciaHandler(pipeline, _servicios[context.ClusterId]), disposeHandler: false);
        }
    }

    private sealed class GatewayConPolly : WebApplicationFactory<Program>
    {
        private readonly ResilienciaOptions _opciones;

        public ServicioDeMentira Usuarios { get; } = new();
        public ServicioDeMentira Requisiciones { get; } = new();

        public GatewayConPolly(ResilienciaOptions opciones) => _opciones = opciones;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                var servicios = new Dictionary<string, ServicioDeMentira>
                {
                    ["usuarios"] = Usuarios,
                    ["requisiciones"] = Requisiciones,
                    ["licitaciones"] = new ServicioDeMentira(),
                    ["recepcion"] = new ServicioDeMentira(),
                };
                services.AddSingleton<IForwarderHttpClientFactory>(new ClienteConPollyFactory(_opciones, servicios));
            });
        }
    }

    private const string RutaUsuarios = "/api/usuarios/prueba";
    private const string RutaRequisiciones = "/api/requisiciones/prueba";

    // Esperas de 1 ms para que los tests no tarden. El breaker pausa 1 segundo (el mínimo que permite Polly en segundos enteros).
    private static ResilienciaOptions Opciones(int reintentos, int minimoPedidos) => new()
    {
        MaxReintentos = reintentos,
        EsperaBaseMs = 1,
        MinimoPedidos = minimoPedidos,
        RatioFallas = 0.5,
        VentanaSegundos = 10,
        PausaSegundos = 1
    };

    private static async Task AssertProblemDetails502Async(HttpResponseMessage respuesta)
    {
        Assert.Equal(HttpStatusCode.BadGateway, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        Assert.Equal(502, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            respuesta.Headers.GetValues("X-Correlation-ID").Single(),
            json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Un_GET_con_el_servicio_caido_se_reintenta_con_retroceso_y_el_cliente_recibe_502_con_ProblemDetails()
    {
        using var gateway = new GatewayConPolly(Opciones(reintentos: 2, minimoPedidos: 100));
        gateway.Usuarios.Caido = true;
        var client = gateway.CreateClient();

        var respuesta = await client.GetAsync(RutaUsuarios);

        await AssertProblemDetails502Async(respuesta);
        // 1 intento inicial + 2 reintentos
        Assert.Equal(3, gateway.Usuarios.Llamadas);
    }

    [Fact]
    public async Task Un_POST_con_el_servicio_caido_NO_se_reintenta()
    {
        using var gateway = new GatewayConPolly(Opciones(reintentos: 2, minimoPedidos: 100));
        gateway.Usuarios.Caido = true;
        var client = gateway.CreateClient();

        var respuesta = await client.PostAsync(RutaUsuarios, new StringContent("{}"));

        await AssertProblemDetails502Async(respuesta);
        Assert.Equal(1, gateway.Usuarios.Llamadas);
    }

    // Polly exige al menos 1 reintento en la configuración, pero un POST nunca se reintenta:
    // en los tests del breaker se usa POST para que cada pedido sea exactamente UN intento.
    private static Task<HttpResponseMessage> Escribir(HttpClient client)
        => client.PostAsync(RutaUsuarios, new StringContent("{}"));

    [Fact]
    public async Task El_circuit_breaker_se_abre_y_el_Gateway_responde_502_sin_llamar_al_servicio()
    {
        using var gateway = new GatewayConPolly(Opciones(reintentos: 1, minimoPedidos: 2));
        gateway.Usuarios.Caido = true;
        var client = gateway.CreateClient();

        // Dos fallas seguidas alcanzan el mínimo de pedidos con 100% de fallas: el breaker se abre.
        await Escribir(client);
        await Escribir(client);
        Assert.Equal(2, gateway.Usuarios.Llamadas);

        var respuesta = await Escribir(client);

        await AssertProblemDetails502Async(respuesta);
        Assert.Equal(2, gateway.Usuarios.Llamadas); // el tercer pedido no llegó al servicio
    }

    [Fact]
    public async Task Si_se_cae_un_servicio_los_demas_siguen_respondiendo()
    {
        using var gateway = new GatewayConPolly(Opciones(reintentos: 1, minimoPedidos: 2));
        gateway.Usuarios.Caido = true;
        var client = gateway.CreateClient();

        await Escribir(client);
        await Escribir(client); // breaker de usuarios abierto

        var respuesta = await client.GetAsync(RutaRequisiciones);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(1, gateway.Requisiciones.Llamadas);
    }

    [Fact]
    public async Task Cuando_el_servicio_vuelve_el_breaker_se_cierra_y_se_recupera()
    {
        using var gateway = new GatewayConPolly(Opciones(reintentos: 1, minimoPedidos: 2));
        gateway.Usuarios.Caido = true;
        var client = gateway.CreateClient();

        await Escribir(client);
        await Escribir(client); // breaker abierto

        gateway.Usuarios.Caido = false;
        await Task.Delay(TimeSpan.FromMilliseconds(1300)); // pasa la pausa del breaker (1 s): deja pasar un pedido de prueba

        var respuesta = await Escribir(client);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
