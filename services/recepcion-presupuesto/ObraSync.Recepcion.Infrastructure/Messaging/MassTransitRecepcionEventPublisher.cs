using MassTransit;
using ObraSync.Contracts.Events;
using ObraSync.Recepcion.Application.Interfaces;
using ObraSync.Recepcion.Domain.Enums;
using RecepcionEntity = ObraSync.Recepcion.Domain.Entities.Recepcion;

namespace ObraSync.Recepcion.Infrastructure.Messaging;

public class MassTransitRecepcionEventPublisher : IRecepcionEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitRecepcionEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
    }

    public async Task PublishRecepcionConfirmadaAsync(
        RecepcionEntity recepcion,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var evento = new RecepcionConfirmadaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            RecepcionId: recepcion.Id,
            ObraId: recepcion.ObraId,
            OrdenCompraId: recepcion.OrdenCompraId,
            NumeroRemito: recepcion.NumeroRemito,
            FechaRecepcion: recepcion.FechaRecepcion.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(recepcion.FechaRecepcion, DateTimeKind.Utc)
                : recepcion.FechaRecepcion.ToUniversalTime(),
            Items: recepcion.Detalles.Select(d => new RecepcionConfirmadaItemEvent(
                d.ArticuloId,
                d.CantidadRecibida,
                d.UnidadMedida,
                d.DescripcionArticulo,
                d.CantidadEsperada,
                d.Conforme,
                d.Observacion)).ToList(),
            OrdenCompleta: recepcion.Estado == EstadoRecepcion.Completa);

        await _publishEndpoint.Publish(evento, context =>
        {
            context.MessageId = evento.EventId;
            context.CorrelationId = evento.CorrelationId;
        }, cancellationToken);
    }

    public async Task PublishRecepcionConProblemaAsync(
        RecepcionEntity recepcion,
        int cantidadFotos,
        Guid correlationId,
        Guid? proveedorId = null,
        CancellationToken cancellationToken = default)
    {
        var evento = new RecepcionConProblemaEvent(
            EventId: Guid.NewGuid(),
            OccurredAt: DateTime.UtcNow,
            CorrelationId: correlationId,
            OrdenCompraId: recepcion.OrdenCompraId,
            RecepcionId: recepcion.Id,
            Motivo: recepcion.MotivoProblema ?? string.Empty,
            Detalle: recepcion.Observaciones,
            CantidadFotos: cantidadFotos,
            ObraId: recepcion.ObraId,
            ProveedorId: proveedorId);

        await _publishEndpoint.Publish(evento, context =>
        {
            context.MessageId = evento.EventId;
            context.CorrelationId = evento.CorrelationId;
        }, cancellationToken);
    }
}
