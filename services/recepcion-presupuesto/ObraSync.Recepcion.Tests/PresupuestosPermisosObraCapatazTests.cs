using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Presupuestos;
using ObraSync.Recepcion.Infrastructure.Persistence;
using Xunit;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Un Capataz solo puede ver el presupuesto de las obras que tiene asignadas (mismo criterio que Recepciones, #189).
/// </summary>
public class PresupuestosPermisosObraCapatazTests : IClassFixture<RecepcionApiFactory>
{
    private readonly RecepcionApiFactory _factory;

    public PresupuestosPermisosObraCapatazTests(RecepcionApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CrearClienteCapataz(Guid obraAsignada)
    {
        var validador = Substitute.For<IObraAsignacionValidator>();
        validador
            .EstaAsignadaACapatazAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<Guid>(0) == obraAsignada));
        validador
            .ObtenerObrasAsignadasAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Guid>?>(new List<Guid> { obraAsignada }));

        var cliente = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObraAsignacionValidator>();
                services.AddScoped<IObraAsignacionValidator>(_ => validador);
            });
        }).CreateClient();

        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", RecepcionApiFactory.CreateToken(role: "Capataz"));
        return cliente;
    }

    private async Task<Guid> SembrarPartidaAsync(Guid obraId)
    {
        var partida = PartidaSemilla.Crear(obraId, "Estructura", 100_000m);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
        db.PartidasPresupuestarias.Add(partida);
        await db.SaveChangesAsync();
        return partida.Id;
    }

    [Fact]
    public async Task Capataz_ConsultaObraAsignada_Retorna200ConSusPartidas()
    {
        var obraPropia = Guid.NewGuid();
        await SembrarPartidaAsync(obraPropia);
        var cliente = CrearClienteCapataz(obraPropia);

        var respuesta = await cliente.GetAsync($"/api/presupuestos/partidas?obraId={obraPropia}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var partidas = await respuesta.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(partidas);
        Assert.NotEmpty(partidas!);
        Assert.All(partidas!, p => Assert.Equal(obraPropia, p.ObraId));
    }

    [Fact]
    public async Task Capataz_ConsultaObraNoAsignada_Retorna403()
    {
        var obraPropia = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();
        await SembrarPartidaAsync(obraAjena);
        var cliente = CrearClienteCapataz(obraPropia);

        var respuesta = await cliente.GetAsync($"/api/presupuestos/partidas?obraId={obraAjena}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Capataz_SinIndicarObra_SoloVeLasPartidasDeSusObras()
    {
        var obraPropia = Guid.NewGuid();
        var obraAjena = Guid.NewGuid();
        await SembrarPartidaAsync(obraPropia);
        await SembrarPartidaAsync(obraAjena);
        var cliente = CrearClienteCapataz(obraPropia);

        var respuesta = await cliente.GetAsync("/api/presupuestos/partidas");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var partidas = await respuesta.Content.ReadFromJsonAsync<List<PartidaPresupuestariaDto>>();
        Assert.NotNull(partidas);
        Assert.Contains(partidas!, p => p.ObraId == obraPropia);
        Assert.DoesNotContain(partidas!, p => p.ObraId == obraAjena);
    }

    [Fact]
    public async Task Capataz_ConsultaPartidaDeObraNoAsignada_Retorna403()
    {
        var obraPropia = Guid.NewGuid();
        var partidaAjenaId = await SembrarPartidaAsync(Guid.NewGuid());
        var cliente = CrearClienteCapataz(obraPropia);

        var respuesta = await cliente.GetAsync($"/api/presupuestos/partidas/{partidaAjenaId}");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Capataz_ConsultaPartidaDeObraAsignada_Retorna200()
    {
        var obraPropia = Guid.NewGuid();
        var partidaId = await SembrarPartidaAsync(obraPropia);
        var cliente = CrearClienteCapataz(obraPropia);

        var respuesta = await cliente.GetAsync($"/api/presupuestos/partidas/{partidaId}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }
}
