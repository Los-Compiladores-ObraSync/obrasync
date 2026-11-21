using MassTransit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Domain.Constants;
using ObraSync.Recepcion.Domain.Entities;
using ObraSync.Recepcion.Domain.Enums;
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

    [Theory]
    [InlineData(EstadoRecepcion.Parcial, false)]
    [InlineData(EstadoRecepcion.Completa, true)]
    public async Task Publica_RecepcionConfirmadaEvent_con_campo_aditivo_OrdenCompleta(
        EstadoRecepcion estado,
        bool ordenCompletaEsperada)
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        RecepcionConfirmadaEvent? publicado = null;
        endpoint.Publish(
            Arg.Do<RecepcionConfirmadaEvent>(e => publicado = e),
            Arg.Any<IPipe<PublishContext<RecepcionConfirmadaEvent>>>(),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var recepcion = CrearRecepcion();
        recepcion.Estado = estado;

        await new MassTransitRecepcionEventPublisher(endpoint)
            .PublishRecepcionConfirmadaAsync(recepcion, Guid.NewGuid());

        Assert.NotNull(publicado);
        Assert.Equal(ordenCompletaEsperada, publicado!.OrdenCompleta);
    }

    [Fact]
    public async Task Publica_RecepcionConProblemaEvent_una_sola_vez_con_motivo_detalle_y_cantidad_de_fotos()
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        IPipe<PublishContext<RecepcionConProblemaEvent>>? pipe = null;
        RecepcionConProblemaEvent? publicado = null;
        endpoint.Publish(
            Arg.Do<RecepcionConProblemaEvent>(e => publicado = e),
            Arg.Do<IPipe<PublishContext<RecepcionConProblemaEvent>>>(p => pipe = p),
            Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var recepcion = CrearRecepcion();
        recepcion.Estado = EstadoRecepcion.Rechazada;
        recepcion.MotivoProblema = MotivosProblemaRecepcion.LlegoDanada;
        recepcion.Observaciones = "Bolsas rotas";
        var correlationId = Guid.NewGuid();
        var proveedorId = Guid.NewGuid();

        await new MassTransitRecepcionEventPublisher(endpoint)
            .PublishRecepcionConProblemaAsync(recepcion, cantidadFotos: 2, correlationId, proveedorId);

        await endpoint.Received(1).Publish(
            Arg.Any<RecepcionConProblemaEvent>(),
            Arg.Any<IPipe<PublishContext<RecepcionConProblemaEvent>>>(),
            Arg.Any<CancellationToken>());

        Assert.NotNull(publicado);
        Assert.NotEqual(Guid.Empty, publicado!.EventId);
        Assert.Equal(DateTimeKind.Utc, publicado.OccurredAt.Kind);
        Assert.Equal(correlationId, publicado.CorrelationId);
        Assert.Equal(recepcion.OrdenCompraId, publicado.OrdenCompraId);
        Assert.Equal(recepcion.Id, publicado.RecepcionId);
        Assert.Equal(recepcion.ObraId, publicado.ObraId);
        Assert.Equal(proveedorId, publicado.ProveedorId);
        Assert.Equal(MotivosProblemaRecepcion.LlegoDanada, publicado.Motivo);
        Assert.Equal("Bolsas rotas", publicado.Detalle);
        Assert.Equal(2, publicado.CantidadFotos);

        Assert.NotNull(pipe);
        var ctx = Substitute.For<PublishContext<RecepcionConProblemaEvent>>();
        await pipe!.Send(ctx);
        Assert.Equal(publicado.EventId, ctx.MessageId);
        Assert.Equal(correlationId, ctx.CorrelationId);
    }
}

