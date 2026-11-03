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
    public async Task Mensaje_nuevo_guarda_la_requisicion_con_sus_items()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);
        var evento = CrearEvento();

        await consumer.Consume(CrearContexto(evento));

        var guardada = await db.RequisicionesAprobadas
            .Include(r => r.Items)
            .SingleAsync(r => r.RequisicionId == evento.RequisicionId);

        Assert.Equal(evento.NumeroRequisicion, guardada.NumeroRequisicion);
        Assert.Equal(evento.ObraId, guardada.ObraId);
        var item = Assert.Single(guardada.Items);
        Assert.Equal(evento.Items[0].ArticuloId, item.ArticuloId);
        Assert.Equal("Cemento", item.Descripcion);
        Assert.Equal(80m, item.Cantidad);
        Assert.Equal("bolsa", item.UnidadMedida);
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
        Assert.Equal(1, await db.RequisicionesAprobadas.CountAsync());
        Assert.Equal(1, await db.RequisicionAprobadaItems.CountAsync());
    }

    [Fact]
    public async Task Misma_requisicion_con_otro_EventId_no_se_guarda_dos_veces()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);
        var evento = CrearEvento();
        var otroEvento = evento with { EventId = Guid.NewGuid() };   // misma requisición, otro EventId

        await consumer.Consume(CrearContexto(evento));
        await consumer.Consume(CrearContexto(otroEvento));

        Assert.Equal(2, await db.EventosProcesados.CountAsync());      // los dos eventos quedan registrados
        Assert.Equal(1, await db.RequisicionesAprobadas.CountAsync()); // pero la requisición es una sola
        Assert.Equal(1, await db.RequisicionAprobadaItems.CountAsync());
    }

    [Fact]
    public async Task Mensaje_con_EventId_vacio_se_descarta()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);

        await consumer.Consume(CrearContexto(CrearEvento(eventId: Guid.Empty)));

        Assert.Equal(0, await db.EventosProcesados.CountAsync());
        Assert.Equal(0, await db.RequisicionesAprobadas.CountAsync());
    }

    [Fact]
    public async Task Mensaje_sin_items_se_descarta()
    {
        await using var db = CrearDb();
        var consumer = new RequisicionAprobadaConsumer(db, NullLogger<RequisicionAprobadaConsumer>.Instance);

        await consumer.Consume(CrearContexto(CrearEvento(conItems: false)));

        Assert.Equal(0, await db.EventosProcesados.CountAsync());
        Assert.Equal(0, await db.RequisicionesAprobadas.CountAsync());
    }
}