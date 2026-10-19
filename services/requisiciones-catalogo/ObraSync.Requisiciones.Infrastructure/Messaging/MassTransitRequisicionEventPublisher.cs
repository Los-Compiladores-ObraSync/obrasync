using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.Interfaces;

namespace ObraSync.Requisiciones.Infrastructure.Messaging
{
    public class MassTransitRequisicionEventPublisher : IRequisicionEventPublisher
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public MassTransitRequisicionEventPublisher(IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
        }

        public async Task PublishRequisicionAprobadaAsync(
            RequisicionAprobadaEvent evento,
            CancellationToken cancellationToken = default)
        {
            await _publishEndpoint.Publish(evento, context =>
            {
                context.MessageId = evento.EventId;
                context.CorrelationId = evento.CorrelationId;
            }, cancellationToken);
        }
    }
}
