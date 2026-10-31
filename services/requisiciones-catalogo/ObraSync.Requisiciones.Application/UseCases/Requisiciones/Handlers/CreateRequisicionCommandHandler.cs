using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.Exceptions;
using ObraSync.Requisiciones.Application.Interfaces;
using ObraSync.Requisiciones.Application.Mappings;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.UseCases.Requisiciones.Handlers
{
    public class CreateRequisicionCommandHandler
    {
        public static readonly TimeSpan VentanaDuplicados = TimeSpan.FromMinutes(2);

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

            var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
                ? null
                : command.IdempotencyKey.Trim();

            if (idempotencyKey != null && idempotencyKey.Length > 100)
                throw new ArgumentException("El encabezado Idempotency-Key no puede superar los 100 caracteres.", nameof(command));

            var articuloIds = command.Items.Select(i => i.ArticuloId).Distinct().ToList();
            if (articuloIds.Any(id => id == Guid.Empty))
                throw new ArgumentException("Todos los ítems deben tener un articuloId válido.", nameof(command));

            var itemsSolicitados = command.Items
                .Select(i => (i.ArticuloId, i.Cantidad))
                .ToList();

            // 1. Alternativa robusta: si se envía Idempotency-Key, devolver la misma respuesta si coincide el envío
            if (idempotencyKey != null)
            {
                var existentePorKey = await _requisicionRepository.GetByIdempotencyKeyAsync(
                    command.SolicitanteId,
                    idempotencyKey,
                    cancellationToken);

                if (existentePorKey != null)
                {
                    if (existentePorKey.CoincideConSolicitud(
                        command.SolicitanteId,
                        command.ObraId,
                        command.Etapa,
                        itemsSolicitados))
                    {
                        return existentePorKey.ToDto();
                    }

                    throw new RequisicionDuplicadaException(
                        existentePorKey.NumeroRequisicion,
                        existentePorKey.Id,
                        $"El encabezado Idempotency-Key '{idempotencyKey}' ya fue utilizado en la requisición '{existentePorKey.NumeroRequisicion}' con datos distintos.");
                }
            }

            // 2. Regla de negocio: mismo solicitante, obra, etapa e ítems/cantidades en ventana de 2 minutos -> 409 Conflict
            var ahoraUtc = DateTime.UtcNow;
            var recientes = await _requisicionRepository.GetRecentBySolicitanteAndObraAsync(
                command.SolicitanteId,
                command.ObraId,
                ahoraUtc.Subtract(VentanaDuplicados),
                cancellationToken);

            var duplicadaReciente = recientes?.FirstOrDefault(r =>
                r.CoincideConSolicitud(
                    command.SolicitanteId,
                    command.ObraId,
                    command.Etapa,
                    itemsSolicitados));

            if (duplicadaReciente != null)
            {
                throw new RequisicionDuplicadaException(
                    duplicadaReciente.NumeroRequisicion,
                    duplicadaReciente.Id,
                    $"Ya existe una requisición idéntica creada recientemente ({duplicadaReciente.NumeroRequisicion}).");
            }

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
            var numeroRequisicion = $"REQ-{ahoraUtc.Year}-{secuencia:D4}";

            var requisicion = new Requisicion(
                requisicionId,
                numeroRequisicion,
                command.ObraId,
                command.SolicitanteId,
                itemsDominio,
                command.Etapa,
                command.Observaciones,
                ahoraUtc,
                idempotencyKey);

            await _requisicionRepository.AddAsync(requisicion, cancellationToken);

            try
            {
                await _requisicionRepository.SaveChangesAsync(cancellationToken);
            }
            catch (Exception) when (idempotencyKey != null)
            {
                var concurrente = await _requisicionRepository.GetByIdempotencyKeyAsync(
                    command.SolicitanteId,
                    idempotencyKey,
                    cancellationToken);

                if (concurrente != null)
                {
                    if (concurrente.CoincideConSolicitud(
                        command.SolicitanteId,
                        command.ObraId,
                        command.Etapa,
                        itemsSolicitados))
                    {
                        return concurrente.ToDto();
                    }

                    throw new RequisicionDuplicadaException(
                        concurrente.NumeroRequisicion,
                        concurrente.Id,
                        $"El encabezado Idempotency-Key '{idempotencyKey}' ya fue utilizado en la requisición '{concurrente.NumeroRequisicion}' con datos distintos.");
                }

                throw;
            }

            return requisicion.ToDto();
        }
    }
}
