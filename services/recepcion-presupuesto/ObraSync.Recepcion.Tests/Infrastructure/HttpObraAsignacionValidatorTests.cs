using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ObraSync.Recepcion.Application.Exceptions;
using ObraSync.Recepcion.Infrastructure.Services;
using Xunit;

namespace ObraSync.Recepcion.Tests.Infrastructure;

public class HttpObraAsignacionValidatorTests
{
    private const string BaseUrl = "http://usuarios-obras-api:8080";

    private static HttpObraAsignacionValidator CrearValidador(FakeHandler handler, ResilienciaHttpOptions? opciones = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["UsuariosObras:BaseUrl"] = BaseUrl })
            .Build();

        var opts = opciones ?? new ResilienciaHttpOptions { EsperaBaseMs = 1 };
        var pipeline = PipelineResilienciaHttp.Crear(opts);

        return new HttpObraAsignacionValidator(configuration, new HttpClient(handler), pipeline: pipeline);
    }

    private static HttpResponseMessage ListaDeObras(params Guid[] ids)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ids.Select(id => new { id }).ToArray())
        };

    [Fact]
    public void SinBaseUrl_LanzaInvalidOperationException()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new HttpObraAsignacionValidator(configuration));
        Assert.Contains("UsuariosObras:BaseUrl", ex.Message);
    }

    [Fact]
    public async Task ObraEnLaLista_DevuelveTrue()
    {
        var obraId = Guid.NewGuid();
        var handler = new FakeHandler(_ => ListaDeObras(Guid.NewGuid(), obraId));

        var asignada = await CrearValidador(handler).EstaAsignadaACapatazAsync(obraId, "Bearer token", "corr-1");

        Assert.True(asignada);
    }

    [Fact]
    public async Task ObraAjena_DevuelveFalse()
    {
        var handler = new FakeHandler(_ => ListaDeObras(Guid.NewGuid()));

        var asignada = await CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1");

        Assert.False(asignada);
    }

    [Fact]
    public async Task ObtenerObrasAsignadas_DevuelveListaDeIds()
    {
        var obra1 = Guid.NewGuid();
        var obra2 = Guid.NewGuid();
        var handler = new FakeHandler(_ => ListaDeObras(obra1, obra2));

        var obras = await CrearValidador(handler).ObtenerObrasAsignadasAsync("Bearer token", "corr-1");

        Assert.NotNull(obras);
        Assert.Equal(2, obras.Count);
        Assert.Contains(obra1, obras);
        Assert.Contains(obra2, obras);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UsuariosRechazaElToken_DevuelveFalse(HttpStatusCode codigo)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(codigo));

        var asignada = await CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1");

        Assert.False(asignada);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UsuariosRespondeConError_LanzaNoVerificable(HttpStatusCode codigo)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(codigo));

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));
    }

    [Fact]
    public async Task UsuariosCaido_LanzaNoVerificable()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("Connection refused"));

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));
    }

    [Fact]
    public async Task UsuariosNoRespondeATiempo_LanzaNoVerificable()
    {
        var handler = new FakeHandler(_ => throw new TaskCanceledException("Se venció el tiempo de espera"));

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));
    }

    [Fact]
    public async Task UsuariosDevuelveUnJsonQueNoSeEntiende_LanzaNoVerificable()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("esto no es json", System.Text.Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<ObraAsignacionNoVerificableException>(
            () => CrearValidador(handler).EstaAsignadaACapatazAsync(Guid.NewGuid(), "Bearer token", "corr-1"));
    }

    [Fact]
    public async Task PropagaElTokenYElCorrelationIdAUsuarios()
    {
        var obraId = Guid.NewGuid();
        var handler = new FakeHandler(_ => ListaDeObras(obraId));

        await CrearValidador(handler).EstaAsignadaACapatazAsync(obraId, "Bearer token-de-prueba", "correlacion-123");

        Assert.Equal($"{BaseUrl}/api/Obras", handler.UrlSolicitada);
        Assert.Equal("Bearer token-de-prueba", handler.Authorization);
        Assert.Equal("correlacion-123", handler.CorrelationId);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respuesta;

        public string? UrlSolicitada { get; private set; }
        public string? Authorization { get; private set; }
        public string? CorrelationId { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respuesta)
        {
            _respuesta = respuesta;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UrlSolicitada = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            CorrelationId = request.Headers.TryGetValues("X-Correlation-ID", out var valores)
                ? valores.FirstOrDefault()
                : null;

            return Task.FromResult(_respuesta(request));
        }
    }
}
