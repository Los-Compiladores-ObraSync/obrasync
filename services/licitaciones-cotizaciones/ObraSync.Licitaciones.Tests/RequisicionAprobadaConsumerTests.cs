using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ObraSync.Contracts.Events;
using ObraSync.Licitaciones.Infrastructure;
using ObraSync.Licitaciones.Infrastructure.Consumers;
using Xunit;

namespace ObraSync.Licitaciones.Tests;

public class RequisicionAprobadaConsumerTests
{
    // Una base en memoria nueva por prueba, para que no se mezclen datos
    private static LicitacionesDbContext CrearDb()
    {
        var options = new DbContextOptionsBuilder<LicitacionesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new LicitacionesDbContext(options);
    }

    private static RequisicionAprobadaEvent CrearEvento(Guid? eventId = null, bool conItems = true)
    {
        var items = conItems
            ? new List<RequisicionAprobadaItemEvent>
              {
                  new(Guid.NewGuid(), "Cemento", 80m, "bolsa")
              }
            : new List<RequisicionAprobadaItemEvent>();

        return new RequisicionAprobadaEvent(
            eventId ?? Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "REQ-001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            items);
    }

    private static ConsumeContext<RequisicionAprobadaEvent> CrearContexto(RequisicionAprobadaEvent msg)
    {
        var context = Substitute.For<ConsumeContext<RequisicionAprobadaEvent>>();
        context.Message.Returns(msg);
        return context;
    }

    [Fact]
    public async Task Mensaje_nuevo_se_procesa_y_se_registra_el_EventId()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);
        var evento = CrearEvento();

        await consumer.Consume(CrearContexto(evento));

        Assert.Equal(1, await db.EventosProcesados.CountAsync());
        Assert.True(await db.EventosProcesados.AnyAsync(e => e.EventId == evento.EventId));
    }

    [Fact]
    public async Task Mensaje_repetido_no_duplica_el_efecto()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);
        var evento = CrearEvento();

        await consumer.Consume(CrearContexto(evento));
        await consumer.Consume(CrearContexto(evento)); // mismo EventId otra vez

        Assert.Equal(1, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task Mensaje_con_EventId_vacio_se_descarta()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);

        await consumer.Consume(CrearContexto(CrearEvento(eventId: Guid.Empty)));

        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }

    [Fact]
    public async Task Mensaje_sin_items_se_descarta()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);

        await consumer.Consume(CrearContexto(CrearEvento(conItems: false)));

        Assert.Equal(0, await db.EventosProcesados.CountAsync());
    }
}