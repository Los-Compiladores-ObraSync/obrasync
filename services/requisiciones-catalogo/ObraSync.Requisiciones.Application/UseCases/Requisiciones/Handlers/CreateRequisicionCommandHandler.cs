using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class CreateRequisicionCommandHandler
    {
        private readonly IRequisicionRepository _requisicionRepository;
        private readonly IInsumoRepository _insumoRepository;

        public CreateRequisicionCommandHandler(
            IRequisicionRepository requisicionRepository,
            IInsumoRepository insumoRepository)
        {
            _requisicionRepository = requisicionRepository ?? throw new ArgumentNullException(nameof(requisicionRepository));
            _insumoRepository = insumoRepository ?? throw new ArgumentNullException(nameof(insumoRepository));
        }

        public async Task<RequisicionResponseDto> Handle(
            CreateRequisicionCommand command,
            CancellationToken cancellationToken = default)
        {
            if (command.ObraId == Guid.Empty)
                throw new ArgumentException("El identificador de la obra (obraId) es obligatorio.", nameof(command));

            if (command.SolicitanteId == Guid.Empty)
                throw new ArgumentException("El identificador del solicitante es obligatorio.", nameof(command));

            if (command.Items == null || command.Items.Count == 0)
                throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(command));

            var articuloIds = command.Items.Select(i => i.ArticuloId).Distinct().ToList();
            if (articuloIds.Any(id => id == Guid.Empty))
                throw new ArgumentException("Todos los ítems deben tener un articuloId válido.", nameof(command));

            var insumos = await _insumoRepository.GetByIdsAsync(articuloIds, cancellationToken);
            var insumosPorId = insumos.ToDictionary(i => i.Id);

            var requisicionId = Guid.NewGuid();
            var itemsDominio = new List<RequisicionItem>();

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

                itemsDominio.Add(new RequisicionItem(
                    Guid.NewGuid(),
                    requisicionId,
                    insumo.Id,
                    insumo.Descripcion,
                    itemDto.Cantidad,
                    insumo.UnidadMedida));
            }

            var secuencia = await _requisicionRepository.GetNextSequenceNumberAsync(cancellationToken);
            var numeroRequisicion = $"REQ-{DateTime.UtcNow.Year}-{secuencia:D4}";

            var requisicion = new Requisicion(
                requisicionId,
                numeroRequisicion,
                command.ObraId,
                command.SolicitanteId,
                itemsDominio,
                command.Etapa,
                command.Observaciones,
                DateTime.UtcNow);

            await _requisicionRepository.AddAsync(requisicion, cancellationToken);
            await _requisicionRepository.SaveChangesAsync(cancellationToken);

            return requisicion.ToDto();
        }
    }
}
