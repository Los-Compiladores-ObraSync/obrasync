using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
using ObraSync.Recepcion.Infrastructure.Persistence;
using ObraSync.Recepcion.Tests.Domain;
using Xunit;

namespace ObraSync.Recepcion.Tests;

/// <summary>
/// Pruebas de integración del flujo completo de recepción parcial (#236):
/// 1) Una recepción parcial no cierra la orden como "Completa", mantiene el remanente abierto
///    y expone EstadoEntrega = "Entrega parcial / Pendiente de re-despacho".
/// 2) Una entrega con problema (Rechazada) pasa a EstadoEntrega = "Con problema" sin sumar acumulados.
/// 3) Al completar el remanente, la orden pasa a Completa y sale de órdenes pendientes.
/// </summary>
public class RecepcionParcialEstadoIntermedioTests
{
    private sealed class FactoryConMocks : RecepcionApiFactory
    {
        public IObraAsignacionValidator Validador { get; } = Substitute.For<IObraAsignacionValidator>();
        public IRecepcionEventPublisher Publisher { get; } = Substitute.For<IRecepcionEventPublisher>();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IObraAsignacionValidator>();
                services.AddSingleton(Validador);
                services.RemoveAll<IRecepcionEventPublisher>();
                services.AddSingleton(Publisher);
            });
        }
    }

    [Fact]
    public async Task FlujoRecepcionParcial_ConProblemaYCompletadoFinal_MantieneRemanenteYEstadoIntermedioHastaCompletar()
    {
        using var factory = new FactoryConMocks();
        var capatazId = Guid.NewGuid();
        var obraId = Guid.NewGuid();
        var ordenId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();

        factory.Validador
            .ObtenerObrasAsignadasAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { obraId });
        factory.Validador
            .EstaAsignadaACapatazAsync(obraId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecepcionDbContext>();
            db.OrdenesCompra.Add(OrdenCompra.Crear(
                id: ordenId,
                obraId: obraId,
                proveedorId: Guid.NewGuid(),
                montoTotal: 1000m,
                items: new[]
                {
                    OrdenCompraItem.Crear(articuloId, 10m, "bolsa", 100m, "Cemento Portland x 25kg")
                },
                etapa: "Estructura",
                fechaDespacho: DateTime.UtcNow.AddHours(-2)));
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            RecepcionApiFactory.CreateToken(role: "Capataz", userId: capatazId));

        // 0) Inicialmente: pendiente sin recepciones
        var pendientesInicial = await client.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(pendientesInicial);
        var ordenInicial = Assert.Single(pendientesInicial, o => o.OrdenCompraId == ordenId);
        Assert.Equal(EstadosEntregaOrden.Pendiente, ordenInicial.EstadoEntrega);
        Assert.Equal(10m, ordenInicial.Items[0].CantidadPendiente);

        // 1) Recepción parcial: llegan 4 de 10 bolsas
        var respParcial = await client.PostAsJsonAsync("/api/recepciones", new CrearRecepcionRequest
        {
            OrdenCompraId = ordenId,
            ObraId = obraId,
            NumeroRemito = "REM-236-1",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento Portland x 25kg",
                    CantidadEsperada = 10m,
                    CantidadRecibida = 4m,
                    UnidadMedida = "bolsa",
                    Conforme = true
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, respParcial.StatusCode);
        var dtoParcial = await respParcial.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dtoParcial);
        Assert.Equal(EstadoRecepcion.Parcial, dtoParcial.Estado);

        // Verifica que sigue abierta en ordenes-pendientes con remanente = 6 y estado intermedio
        var pendientesLuegoParcial = await client.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(pendientesLuegoParcial);
        var ordenParcial = Assert.Single(pendientesLuegoParcial, o => o.OrdenCompraId == ordenId);
        Assert.Equal(EstadosEntregaOrden.EntregaParcial, ordenParcial.EstadoEntrega);
        Assert.False(ordenParcial.TieneProblemaAbierto);
        Assert.Equal(4m, ordenParcial.Items[0].CantidadRecibida);
        Assert.Equal(6m, ordenParcial.Items[0].CantidadPendiente);

        var resumenParcial = await client.GetFromJsonAsync<RecepcionesDeOrdenDto>($"/api/recepciones/ordenes/{ordenId}");
        Assert.NotNull(resumenParcial);
        Assert.False(resumenParcial.EstaCompleta);
        Assert.Equal(EstadosEntregaOrden.EntregaParcial, resumenParcial.EstadoEntrega);

        // 2) Entrega con problema sobre el remanente -> NO cierra ni altera acumulado (sigue pendiente = 6)
        using var formProblema = new MultipartFormDataContent();
        formProblema.Add(new StringContent(obraId.ToString()), "obraId");
        formProblema.Add(new StringContent(ordenId.ToString()), "ordenCompraId");
        formProblema.Add(new StringContent(MotivosProblemaRecepcion.LlegoIncompleta), "motivo");
        formProblema.Add(new StringContent("Faltaron bolsas en el segundo viaje"), "observaciones");

        var respProblema = await client.PostAsync("/api/recepciones/problemas", formProblema);
        Assert.Equal(HttpStatusCode.Created, respProblema.StatusCode);

        var pendientesConProblema = await client.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(pendientesConProblema);
        var ordenConProblema = Assert.Single(pendientesConProblema, o => o.OrdenCompraId == ordenId);
        Assert.True(ordenConProblema.TieneProblemaAbierto);
        Assert.Equal(EstadosEntregaOrden.ConProblema, ordenConProblema.EstadoEntrega);
        Assert.Equal(4m, ordenConProblema.Items[0].CantidadRecibida);
        Assert.Equal(6m, ordenConProblema.Items[0].CantidadPendiente);

        // 3) Re-entrega final completando las 6 bolsas restantes -> Completa y sale de pendientes
        var respFinal = await client.PostAsJsonAsync("/api/recepciones", new CrearRecepcionRequest
        {
            OrdenCompraId = ordenId,
            ObraId = obraId,
            NumeroRemito = "REM-236-2",
            FechaRecepcion = DateTime.UtcNow,
            Detalles = new List<CrearDetalleRecepcionRequest>
            {
                new()
                {
                    ArticuloId = articuloId,
                    DescripcionArticulo = "Cemento Portland x 25kg",
                    CantidadEsperada = 6m,
                    CantidadRecibida = 6m,
                    UnidadMedida = "bolsa",
                    Conforme = true
                }
            }
        });
        Assert.Equal(HttpStatusCode.Created, respFinal.StatusCode);
        var dtoFinal = await respFinal.Content.ReadFromJsonAsync<RecepcionDto>();
        Assert.NotNull(dtoFinal);
        Assert.Equal(EstadoRecepcion.Completa, dtoFinal.Estado);

        var pendientesFinal = await client.GetFromJsonAsync<List<OrdenPendienteDto>>("/api/recepciones/ordenes-pendientes");
        Assert.NotNull(pendientesFinal);
        Assert.DoesNotContain(pendientesFinal, o => o.OrdenCompraId == ordenId);

        var resumenFinal = await client.GetFromJsonAsync<RecepcionesDeOrdenDto>($"/api/recepciones/ordenes/{ordenId}");
        Assert.NotNull(resumenFinal);
        Assert.True(resumenFinal.EstaCompleta);
        Assert.False(resumenFinal.TieneProblemaAbierto);
        Assert.Equal(EstadosEntregaOrden.Completa, resumenFinal.EstadoEntrega);
        Assert.Equal(10m, resumenFinal.Items[0].CantidadRecibida);
        Assert.Equal(0m, resumenFinal.Items[0].CantidadPendiente);
    }
}
