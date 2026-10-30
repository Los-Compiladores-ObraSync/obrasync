using MassTransit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Infrastructure.Messaging;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Tests.Events;

public class RecepcionConfirmadaPublisherTests
{
    private static RecepcionEntity CrearRecepcion()
    {
        var r = new RecepcionEntity
        {
            Id = Guid.NewGuid(),
            ObraId = Guid.NewGuid(),
            OrdenCompraId = Guid.NewGuid(),
            NumeroRemito = "REM-001",
            FechaRecepcion = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        };
        r.Detalles.Add(new DetalleRecepcion
        {
            ArticuloId = Guid.NewGuid(),
            DescripcionArticulo = "Cemento",
            CantidadEsperada = 10,
            CantidadRecibida = 10,
            UnidadMedida = "BOLSA",
            Conforme = true,
            Observacion = "ok"
        });
        return r;
    }

    [Fact]
    public async Task Publica_una_vez_con_los_datos_mapeados_y_MessageId_y_CorrelationId()
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        IPipe<PublishContext<RecepcionConfirmadaEvent>>? pipe = null;
        RecepcionConfirmadaEvent? publicado = null;
        endpoint.Publish(
            Arg.Do<RecepcionConfirmadaEvent>(e => publicado = e),
            Arg.Do<IPipe<PublishContext<RecepcionConfirmadaEvent>>>(p => pipe = p),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var recepcion = CrearRecepcion();
        var correlationId = Guid.NewGuid();

        await new MassTransitRecepcionEventPublisher(endpoint)
            .PublishRecepcionConfirmadaAsync(recepcion, correlationId);

        await endpoint.Received(1).Publish(
            Arg.Any<RecepcionConfirmadaEvent>(),
            Arg.Any<IPipe<PublishContext<RecepcionConfirmadaEvent>>>(),
            Arg.Any<CancellationToken>());

        Assert.NotNull(publicado);
        Assert.NotEqual(Guid.Empty, publicado!.EventId);
        Assert.Equal(correlationId, publicado.CorrelationId);
        Assert.Equal(recepcion.Id, publicado.RecepcionId);
        Assert.Equal(recepcion.ObraId, publicado.ObraId);
        Assert.Equal(recepcion.OrdenCompraId, publicado.OrdenCompraId);
        Assert.Equal("REM-001", publicado.NumeroRemito);
        var item = Assert.Single(publicado.Items);
        var detalle = recepcion.Detalles.Single();
        Assert.Equal(detalle.ArticuloId, item.ArticuloId);
        Assert.Equal(10, item.CantidadRecibida);
        Assert.Equal(10, item.CantidadEsperada);
        Assert.Equal("BOLSA", item.UnidadMedida);
        Assert.True(item.Conforme);

        // MessageId = EventId y CorrelationId del contexto
        Assert.NotNull(pipe);
        var ctx = Substitute.For<PublishContext<RecepcionConfirmadaEvent>>();
        await pipe!.Send(ctx);
        Assert.Equal(publicado.EventId, ctx.MessageId);
        Assert.Equal(correlationId, ctx.CorrelationId);
    }

    [Fact]
    public async Task Si_Publish_lanza_la_excepcion_se_propaga()
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint.Publish(
            Arg.Any<RecepcionConfirmadaEvent>(),
            Arg.Any<IPipe<PublishContext<RecepcionConfirmadaEvent>>>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("rabbit caído"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MassTransitRecepcionEventPublisher(endpoint)
                .PublishRecepcionConfirmadaAsync(CrearRecepcion(), Guid.NewGuid()));
    }
}

