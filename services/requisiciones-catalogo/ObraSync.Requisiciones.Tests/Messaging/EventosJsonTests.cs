using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Infrastructure.Messaging;
using Xunit;

namespace ObraSync.Requisiciones.Tests;

/// <summary>
/// Tarjeta #128: los decimales de los eventos tienen que viajar como NÚMERO JSON (como dice contracts/events),
/// no como texto. Se publica por un bus de prueba (en memoria) configurado con la misma extensión que usa el servicio
/// y se mira el cuerpo real del mensaje, con el serializador por defecto de MassTransit (el mismo que usa RabbitMQ).
/// </summary>
public class EventosJsonTests
{
    [Fact]
    public async Task RequisicionAprobadaEvent_viaja_con_los_decimales_como_numero()
    {
        var captura = new CapturaEventosJson();

        await using var provider = new ServiceCollection()
            .AddSingleton(captura)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CapturaEventoConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UsarDecimalesComoNumero();
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new RequisicionAprobadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            NumeroRequisicion: "REQ-0001",
            ObraId: Guid.NewGuid(),
            SolicitanteId: Guid.NewGuid(),
            AprobadorId: Guid.NewGuid(),
            Items: new List<RequisicionAprobadaItemEvent>
            {
                new(Guid.NewGuid(), "Cemento", 10.5m, "BOLSA")
            },
            Etapa: "Fundaciones"));

        Assert.True(await captura.EsperarMensajeAsync(), "El consumidor no recibió el evento a tiempo.");
        Assert.True(captura.Cuerpos.TryDequeue(out var json), "El consumidor no capturó el cuerpo.");

        using var doc = JsonDocument.Parse(json);
        var mensaje = doc.RootElement.GetProperty("message");
        var item = mensaje.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Number, item.GetProperty("cantidad").ValueKind);
    }

    [Fact]
    public async Task RequisicionAprobadaEvent_se_lee_igual_en_el_consumidor()
    {
        var captura = new CapturaEventosJson();

        await using var provider = new ServiceCollection()
            .AddSingleton(captura)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CapturaEventoConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UsarDecimalesComoNumero();
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new RequisicionAprobadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RequisicionId: Guid.NewGuid(),
            NumeroRequisicion: "REQ-0001",
            ObraId: Guid.NewGuid(),
            SolicitanteId: Guid.NewGuid(),
            AprobadorId: Guid.NewGuid(),
            Items: new List<RequisicionAprobadaItemEvent>
            {
                new(Guid.NewGuid(), "Cemento", 10.5m, "BOLSA")
            },
            Etapa: "Fundaciones"));

        Assert.True(await captura.EsperarMensajeAsync(), "El consumidor no recibió el evento a tiempo.");
        Assert.True(captura.Mensajes.TryDequeue(out var recibido));
        Assert.Equal(10.5m, recibido!.Items[0].Cantidad);
    }
}

public class CapturaEventosJson
{
    public ConcurrentQueue<string> Cuerpos { get; } = new();

    /// <summary>
    /// Espera (hasta 15 s) a que el consumidor reciba el mensaje. Se usa en lugar de harness.Consumed.Any,
    /// que se rinde tras ~1,2 s sin actividad y falla en la CI cuando el runner arranca lento.
    /// </summary>
    public async Task<bool> EsperarMensajeAsync()
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite)
        {
            if (!Cuerpos.IsEmpty && !Mensajes.IsEmpty) return true;
            await Task.Delay(50);
        }
        return !Cuerpos.IsEmpty && !Mensajes.IsEmpty;
    }
    public ConcurrentQueue<RequisicionAprobadaEvent> Mensajes { get; } = new();
}

public class CapturaEventoConsumer : IConsumer<RequisicionAprobadaEvent>
{
    private readonly CapturaEventosJson _captura;

    public CapturaEventoConsumer(CapturaEventosJson captura) => _captura = captura;

    public Task Consume(ConsumeContext<RequisicionAprobadaEvent> context)
    {
        _captura.Cuerpos.Enqueue(Encoding.UTF8.GetString(context.ReceiveContext.GetBody()));
        _captura.Mensajes.Enqueue(context.Message);
        return Task.CompletedTask;
    }
}
