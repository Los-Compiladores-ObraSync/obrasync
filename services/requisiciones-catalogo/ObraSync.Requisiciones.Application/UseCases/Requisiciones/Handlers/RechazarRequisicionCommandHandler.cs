using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Constants;

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
            // Validamos el motivo antes de consultar el repositorio para responder 400 ante payload inválido.
            var motivoValidado = MotivosRechazoRequisicion.ResolverYValidar(
                command.Motivo,
                command.CategoriaMotivo,
                command.Comentario);

            var requisicion = await _repository.GetByIdAsync(command.RequisicionId, cancellationToken);
            if (requisicion == null)
            {
                throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");
            }

            requisicion.Rechazar(command.RechazadorId, motivoValidado, DateTime.UtcNow);

            await _repository.SaveChangesAsync(cancellationToken);

            return requisicion.ToDto();
        }
    }
}
