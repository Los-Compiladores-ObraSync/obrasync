using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Constants;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class ReenviarRequisicionCommandHandler
    {
        private readonly IRequisicionRepository _requisicionRepository;
        private readonly IInsumoRepository _insumoRepository;

        public ReenviarRequisicionCommandHandler(
            IRequisicionRepository requisicionRepository,
            IInsumoRepository insumoRepository)
        {
            _requisicionRepository = requisicionRepository ?? throw new ArgumentNullException(nameof(requisicionRepository));
            _insumoRepository = insumoRepository ?? throw new ArgumentNullException(nameof(insumoRepository));
        }

        public async Task<RequisicionResponseDto> Handle(
            ReenviarRequisicionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (command.RequisicionId == Guid.Empty)
                throw new ArgumentException("El identificador de la requisición es obligatorio.", nameof(command));

            if (command.UsuarioId == Guid.Empty)
                throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(command));

            if (command.Items == null || command.Items.Count == 0)
                throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(command));

            var articuloIds = command.Items.Select(i => i.ArticuloId).Distinct().ToList();
            if (articuloIds.Any(id => id == Guid.Empty))
                throw new ArgumentException("Todos los ítems deben tener un articuloId válido.", nameof(command));

            var requisicion = await _requisicionRepository.GetByIdAsync(command.RequisicionId, cancellationToken);
            if (requisicion == null)
            {
                throw new KeyNotFoundException($"No se encontró la requisición con Id '{command.RequisicionId}'.");
            }

            // Solo el Capataz dueño (SolicitanteId) o un Admin pueden editar y reenviar
            if (!command.EsAdmin && requisicion.SolicitanteId != command.UsuarioId)
            {
                throw new UnauthorizedAccessException("Solo el capataz solicitante dueño de la requisición o un administrador pueden editarla y reenviarla.");
            }

            // Solo se permite editar una requisición en estado Rechazada
            if (!string.Equals(requisicion.Estado, EstadoRequisicion.Rechazada, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Solo se pueden editar y reenviar requisiciones en estado '{EstadoRequisicion.Rechazada}'. Estado actual: '{requisicion.Estado}'.");
            }

            var insumos = await _insumoRepository.GetByIdsAsync(articuloIds, cancellationToken);
            var insumosPorId = insumos.ToDictionary(i => i.Id);

            var nuevosItemsDominio = new List<RequisicionItem>(command.Items.Count);
            foreach (var itemDto in command.Items)
            {
                if (!insumosPorId.TryGetValue(itemDto.ArticuloId, out var insumo))
                {
                    throw new KeyNotFoundException($"No se encontró el artículo con Id '{itemDto.ArticuloId}' en el catálogo.");
                }

                if (!insumo.Activo)
                {
                    throw new ArgumentException($"El artículo '{insumo.Codigo} - {insumo.Descripcion}' se encuentra inactivo y no puede solicitarse.");
                }

                nuevosItemsDominio.Add(new RequisicionItem(
                    Guid.NewGuid(),
                    requisicion.Id,
                    insumo.Id,
                    insumo.Descripcion,
                    itemDto.Cantidad,
                    insumo.UnidadMedida));
            }

            requisicion.Reenviar(nuevosItemsDominio, command.Etapa, command.Observaciones);

            await _requisicionRepository.SaveChangesAsync(cancellationToken);

            return requisicion.ToDto();
        }
    }
}
