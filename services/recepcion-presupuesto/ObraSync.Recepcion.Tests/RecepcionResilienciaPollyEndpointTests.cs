using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Infrastructure.Services;
using Polly;
using Xunit;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Pruebas de integración de extremo a extremo para la resiliencia HTTP con Polly v8 (#103):
/// Verifica que las llamadas HTTP a Usuarios y Obras a través de endpoints de la API (RecepcionesController):
/// 1. Se reintentan y se recuperan de fallos transitorios (503) respondiendo 200 OK al cliente.
/// 2. Cuando el circuit breaker se abre tras fallas reiteradas, cortan de inmediato respondiendo 503 ProblemDetails sin salir a la red.
/// 3. Los errores 401/403 no se reintentan.
/// </summary>
public class RecepcionResilienciaPollyEndpointTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public RecepcionResilienciaPollyEndpointTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private sealed class ServicioMockHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _respuestas = new();
        public int Llamadas { get; private set; }

        public ServicioMockHandler Responde(HttpStatusCode estado, object? body = null)
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            if (_respuestas.Count == 0)
            {
                throw new InvalidOperationException("No hay más respuestas en ServicioMockHandler.");
            }

            var func = _respuestas.Count > 1 ? _respuestas.Dequeue() : _respuestas.Peek();
            return Task.FromResult(func());
        }
    }

    private WebApplicationFactory<Program> CrearFactoryConHandler(
        HttpMessageHandler handler,
        ResilienciaHttpOptions? opciones = null,
        ResiliencePipeline<HttpResponseMessage>? pipelineExistente = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UsuariosObras:BaseUrl", "http://usuarios-obras-test:8080");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObraAsignacionValidator>();
                services.RemoveAll<ResiliencePipeline<HttpResponseMessage>>();

                var opts = opciones ?? new ResilienciaHttpOptions { EsperaBaseMs = 1, MaxReintentos = 3 };
                var pipeline = pipelineExistente ?? PipelineResilienciaHttp.Crear(opts);

                services.AddSingleton(pipeline);
                services.AddScoped<IObraAsignacionValidator>(sp =>
                    new HttpObraAsignacionValidator(
                        sp.GetRequiredService<IConfiguration>(),
                        new HttpClient(handler),
                        sp.GetService<ILogger<HttpObraAsignacionValidator>>(),
                        pipeline));
            });
        });
    }

    [Fact]
    public async Task GetRecepciones_FalloTransitorio503_SeReintentaYResponde200Ok()
    {
        var obraId = Guid.NewGuid();
        var handler = new ServicioMockHandler();
        handler
            .Responde(HttpStatusCode.ServiceUnavailable) // Intento 1: falla transitoria
            .Responde(HttpStatusCode.OK, new[] { new { id = obraId } }); // Intento 2: se recupera

        var factory = CrearFactoryConHandler(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync("/api/recepciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Debe haber ejecutado 2 llamadas HTTP (el reintento exitoso)
        Assert.Equal(2, handler.Llamadas);
    }

    [Fact]
    public async Task GetRecepciones_CircuitBreakerAbierto_Devuelve503ProblemDetailsSinLlamarALaRed()
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
        var handler = new ServicioMockHandler();
        handler.Responde(HttpStatusCode.ServiceUnavailable);

        var factory = CrearFactoryConHandler(handler, opciones, pipelineCompartido);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        // Llamada 1: falla 503
        var r1 = await client.GetAsync("/api/recepciones");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r1.StatusCode);
        Assert.Equal(1, handler.Llamadas);

        // Llamada 2: falla 503 (cumple MinimoPedidos y abre el breaker)
        var r2 = await client.GetAsync("/api/recepciones");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r2.StatusCode);
        Assert.Equal(2, handler.Llamadas);

        // Llamada 3: el circuit breaker ya está abierto
        var r3 = await client.GetAsync("/api/recepciones");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r3.StatusCode);
        Assert.Equal("application/problem+json", r3.Content.Headers.ContentType?.MediaType);

        // El circuit breaker cortó el pedido antes de salir a la red
        Assert.Equal(2, handler.Llamadas);

        using var doc = JsonDocument.Parse(await r3.Content.ReadAsStringAsync());
        Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Servicio no disponible", doc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task GetRecepciones_Error401EnUsuarios_NoSeReintenta()
    {
        var handler = new ServicioMockHandler();
        handler.Responde(HttpStatusCode.Unauthorized);

        var factory = CrearFactoryConHandler(handler);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));

        var response = await client.GetAsync("/api/recepciones");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<RecepcionDto>>();
        Assert.NotNull(items);
        Assert.Empty(items);
        // Exactamente 1 intento, no reintenta errores 401
        Assert.Equal(1, handler.Llamadas);
    }
}
