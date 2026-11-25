using MassTransit;
using ObraSync.Licitaciones.Application.Interfaces;

namespace ObraSync.Licitaciones.Infrastructure.Messaging;

public sealed class MassTransitLicitacionEventPublisher : ILicitacionEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitLicitacionEventPublisher(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
    }

    public Task PublishAsync<TEvent>(TEvent evento, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(evento);
        return _publishEndpoint.Publish<TEvent>(evento, cancellationToken);
    }
}
