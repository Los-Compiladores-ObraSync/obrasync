using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class RechazarRequisicionCommandHandler
    {
        private readonly IRequisicionRepository _repository;

        public RechazarRequisicionCommandHandler(IRequisicionRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<RequisicionResponseDto> Handle(
            RechazarRequisicionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(command.Motivo))
            {
                throw new ArgumentException("El motivo de rechazo es obligatorio.", nameof(command));
            }

            var requisicion = await _repository.GetByIdAsync(command.RequisicionId, cancellationToken);
            if (requisicion == null)
            {
                throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");
            }

            requisicion.Rechazar(command.RechazadorId, command.Motivo, DateTime.UtcNow);

            await _repository.SaveChangesAsync(cancellationToken);

            return requisicion.ToDto();
        }
    }
}
