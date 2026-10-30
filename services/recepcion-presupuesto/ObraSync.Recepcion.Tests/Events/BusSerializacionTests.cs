using System.Text.Json;
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

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x =>
            {
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.UseRawJsonSerializer();
                    cfg.ConfigureJsonSerializerOptions(opts =>
                    {
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

        var json = JsonSerializer.Serialize(evento, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        _output.WriteLine(json);

        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Number, item.GetProperty("cantidadRecibida").ValueKind);
    }
}