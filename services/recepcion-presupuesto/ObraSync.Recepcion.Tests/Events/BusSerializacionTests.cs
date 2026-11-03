using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MassTransit;
using MassTransit.Testing;
using ObraSync.Contracts.Events;
using Xunit;
using Xunit.Abstractions;
using Microsoft.Extensions.DependencyInjection;
namespace ObraSync.Recepcion.Tests.Events;

public class BusSerializacionTests
{
    private readonly ITestOutputHelper _output;

    public BusSerializacionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Bus_serializa_cantidades_como_numero()
    {
        var evento = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: Guid.NewGuid(),
            RecepcionId: Guid.NewGuid(),
            ObraId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            NumeroRemito: "R-TEST",
            FechaRecepcion: DateTime.UtcNow,
            Items: new List<RecepcionConfirmadaItemEvent>
            {
                new(Guid.NewGuid(), 10m, "UN", "Articulo", 10.0000m, true, null)
            });

        var capturador = new CapturaCuerpo();

        await using var provider = new ServiceCollection()
            .AddSingleton(capturador)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<CapturaCuerpoConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseRawJsonSerializer();
                    // Misma configuración que Program.cs (fix #128).
                    cfg.ConfigureJsonSerializerOptions(opts =>
                    {
                        for (var i = opts.Converters.Count - 1; i >= 0; i--)
                        {
                            if (opts.Converters[i].GetType().Name == "StringDecimalJsonConverter")
                                opts.Converters.RemoveAt(i);
                        }

                        opts.NumberHandling = JsonNumberHandling.AllowReadingFromString;
                        _output.WriteLine($"NumberHandling={opts.NumberHandling}");
                        _output.WriteLine($"Converters={string.Join(",", opts.Converters.Select(c => c.GetType().Name))}");
                        return opts;
                    });
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(evento);

        Assert.True(await harness.Published.Any<RecepcionConfirmadaEvent>());

        // Se mide el cuerpo que produjo el bus (lo que recibe el consumidor), no opciones planas.
        // El harness no expone el payload serializado de lo publicado; por eso se usa
        // UseRawJsonSerializer (el cuerpo es el JSON del mensaje sin sobre) y un consumidor
        // que captura los bytes del ReceiveContext.
        Assert.True(await harness.Consumed.Any<RecepcionConfirmadaEvent>());
        Assert.True(capturador.Cuerpos.TryDequeue(out var json), "El consumidor no capturó el cuerpo.");
        _output.WriteLine(json);

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Number, item.GetProperty("cantidadRecibida").ValueKind);
        Assert.Equal(JsonValueKind.Number, item.GetProperty("cantidadEsperada").ValueKind);
    }

    [Fact]
    public void Lectura_acepta_decimales_que_llegan_como_texto()
    {
        const string json = """
        {
          "eventId": "11111111-1111-1111-1111-111111111111",
          "occurredAt": "2026-01-01T00:00:00Z",
          "correlationId": "22222222-2222-2222-2222-222222222222",
          "recepcionId": "33333333-3333-3333-3333-333333333333",
          "obraId": "44444444-4444-4444-4444-444444444444",
          "ordenCompraId": "55555555-5555-5555-5555-555555555555",
          "numeroRemito": "R-1",
          "fechaRecepcion": "2026-01-01T00:00:00Z",
          "items": [
            { "articuloId": "66666666-6666-6666-6666-666666666666", "cantidadRecibida": "10", "unidadMedida": "UN" }
          ]
        }
        """;

        // Mismas opciones de lectura que el bus (fix #128).
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        var evento = JsonSerializer.Deserialize<RecepcionConfirmadaEvent>(json, opciones);

        Assert.NotNull(evento);
        Assert.Equal(10m, evento!.Items[0].CantidadRecibida);
    }
}

public class CapturaCuerpo
{
    public ConcurrentQueue<string> Cuerpos { get; } = new();
}

public class CapturaCuerpoConsumer : IConsumer<RecepcionConfirmadaEvent>
{
    private readonly CapturaCuerpo _captura;

    public CapturaCuerpoConsumer(CapturaCuerpo captura) => _captura = captura;

    public Task Consume(ConsumeContext<RecepcionConfirmadaEvent> context)
    {
        _captura.Cuerpos.Enqueue(Encoding.UTF8.GetString(context.ReceiveContext.GetBody()));
        return Task.CompletedTask;
    }
}