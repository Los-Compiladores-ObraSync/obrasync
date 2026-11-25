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
    /// <summary>
    /// Cancela una requisición mientras está Pendiente. No publica ningún evento: Licitaciones recién se entera
    /// cuando se aprueba, así que una requisición pendiente cancelada nunca llegó a ningún otro servicio.
    /// </summary>
    public class CancelarRequisicionCommandHandler : ICommandHandler<CancelarRequisicionCommand, RequisicionResponseDto>
    {
        private readonly IRequisicionRepository _requisicionRepository;

        public CancelarRequisicionCommandHandler(IRequisicionRepository requisicionRepository)
        {
            _requisicionRepository = requisicionRepository ?? throw new ArgumentNullException(nameof(requisicionRepository));
        }

        public async Task<RequisicionResponseDto> Handle(
            CancelarRequisicionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (command.RequisicionId == Guid.Empty)
                throw new ArgumentException("El identificador de la requisición es obligatorio.", nameof(command));

            if (command.UsuarioId == Guid.Empty)
                throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(command));

            var requisicion = await _requisicionRepository.GetByIdAsync(command.RequisicionId, cancellationToken)
                ?? throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");

            requisicion.AsegurarPuedeSerModificadaPor(command.UsuarioId, command.EsAdmin, "cancelarla");
            requisicion.Cancelar();

            await _requisicionRepository.SaveChangesAsync(cancellationToken);

            return requisicion.ToDto();
        }
    }
}
