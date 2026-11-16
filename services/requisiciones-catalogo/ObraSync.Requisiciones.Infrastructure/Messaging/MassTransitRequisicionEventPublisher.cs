using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Infrastructure.Persistence;

namespace ObraSync.Requisiciones.Infrastructure.Messaging
{
    public class MassTransitRequisicionEventPublisher : IRequisicionEventPublisher
    {
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly RequisicionesDbContext? _dbContext;

        public MassTransitRequisicionEventPublisher(
            IPublishEndpoint publishEndpoint,
            RequisicionesDbContext? dbContext = null)
        {
            _publishEndpoint = publishEndpoint ?? throw new ArgumentNullException(nameof(publishEndpoint));
            _dbContext = dbContext;
        }

        public async Task PublishRequisicionAprobadaAsync(
            RequisicionAprobadaEvent evento,
            CancellationToken cancellationToken = default)
        {
            if (_dbContext != null && !_dbContext.Database.IsRelational())
            {
                // En entorno no relacional (tests InMemory sin Outbox SQL), se difiere la publicación
                // hasta que SaveChangesAsync valide el token de concurrencia RowVersion (#187).
                _dbContext.EncolarAccionPostGuardado(ct =>
                    _publishEndpoint.Publish(evento, context =>
                    {
                        context.MessageId = evento.EventId;
                        context.CorrelationId = evento.CorrelationId;
                    }, ct));
                return;
            }

            await _publishEndpoint.Publish(evento, context =>
            {
                context.MessageId = evento.EventId;
                context.CorrelationId = evento.CorrelationId;
            }, cancellationToken);
        }
    }
}
