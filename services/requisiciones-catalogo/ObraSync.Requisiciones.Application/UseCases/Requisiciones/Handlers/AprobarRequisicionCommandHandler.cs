using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class AprobarRequisicionCommandHandler
    {
        private readonly IRequisicionRepository _repository;
        private readonly IRequisicionEventPublisher _eventPublisher;

        public AprobarRequisicionCommandHandler(
            IRequisicionRepository repository,
            IRequisicionEventPublisher eventPublisher)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        }

        public async Task<RequisicionResponseDto> Handle(
            AprobarRequisicionCommand command,
            CancellationToken cancellationToken = default)
        {
            var requisicion = await _repository.GetByIdAsync(command.RequisicionId, cancellationToken);
            if (requisicion == null)
            {
                throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");
            }

            var fechaAprobacion = DateTime.UtcNow;
            requisicion.Aprobar(command.AprobadorId, fechaAprobacion);

            await _repository.SaveChangesAsync(cancellationToken);

            var correlationId = command.CorrelationId == Guid.Empty
                ? Guid.NewGuid()
                : command.CorrelationId;

            var evento = new RequisicionAprobadaEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: requisicion.FechaAprobacion ?? fechaAprobacion,
                CorrelationId: correlationId,
                RequisicionId: requisicion.Id,
                NumeroRequisicion: requisicion.NumeroRequisicion,
                ObraId: requisicion.ObraId,
                SolicitanteId: requisicion.SolicitanteId,
                AprobadorId: command.AprobadorId,
                Items: requisicion.Items.Select(i => new RequisicionAprobadaItemEvent(
                    i.ArticuloId,
                    i.Descripcion,
                    i.Cantidad,
                    i.UnidadMedida)).ToList());

            await _eventPublisher.PublishRequisicionAprobadaAsync(evento, cancellationToken);

            return requisicion.ToDto();
        }
    }
}
