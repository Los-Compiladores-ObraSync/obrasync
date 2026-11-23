using System;
using System.Linq;
using ObraSync.Contracts.Events;
using ObraSync.Requisiciones.Application.DTOs;
using ObraSync.Requisiciones.Application.UseCases.Requisiciones.Commands;
using ObraSync.Requisiciones.Domain.Entities;

namespace ObraSync.Requisiciones.Application.Mappings
{
    public static class RequisicionMappingExtensions
    {
        public static RequisicionResponseDto ToDto(this Requisicion requisicion)
        {
            ArgumentNullException.ThrowIfNull(requisicion);

            return new RequisicionResponseDto
            {
                RequisicionId = requisicion.Id,
                NumeroRequisicion = requisicion.NumeroRequisicion,
                ObraId = requisicion.ObraId,
                Etapa = requisicion.Etapa,
                SolicitanteId = requisicion.SolicitanteId,
                AprobadorId = requisicion.AprobadorId,
                RechazadorId = requisicion.RechazadorId,
                Estado = requisicion.Estado,
                Observaciones = requisicion.Observaciones,
                MotivoRechazo = requisicion.MotivoRechazo,
                FechaCreacion = requisicion.FechaCreacion,
                FechaAprobacion = requisicion.FechaAprobacion,
                FechaRechazo = requisicion.FechaRechazo,
                Items = (requisicion.Items ?? Enumerable.Empty<RequisicionItem>())
                    .Select(i => i.ToDto())
                    .ToList(),
                HistorialRechazos = (requisicion.HistorialRechazos ?? Enumerable.Empty<HistorialRechazoRequisicion>())
                    .OrderBy(h => h.FechaRechazo)
                    .ThenBy(h => h.Id)
                    .Select(h => h.ToDto())
                    .ToList()
            };
        }

        public static RequisicionItemDto ToDto(this RequisicionItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new RequisicionItemDto
            {
                ArticuloId = item.ArticuloId,
                Descripcion = item.Descripcion,
                Cantidad = item.Cantidad,
                UnidadMedida = item.UnidadMedida
            };
        }

        public static HistorialRechazoRequisicionDto ToDto(this HistorialRechazoRequisicion historial)
        {
            ArgumentNullException.ThrowIfNull(historial);

            return new HistorialRechazoRequisicionDto
            {
                Id = historial.Id,
                RechazadorId = historial.RechazadorId,
                Motivo = historial.Motivo,
                FechaRechazo = historial.FechaRechazo
            };
        }

        public static RequisicionAprobadaEvent ToAprobadaEvent(
            this Requisicion requisicion,
            Guid correlationId,
            Guid aprobadorId,
            DateTime? occurredAt = null)
        {
            ArgumentNullException.ThrowIfNull(requisicion);

            return new RequisicionAprobadaEvent(
                EventId: Guid.NewGuid(),
                OccurredAt: requisicion.FechaAprobacion ?? occurredAt ?? DateTime.UtcNow,
                CorrelationId: correlationId,
                RequisicionId: requisicion.Id,
                NumeroRequisicion: requisicion.NumeroRequisicion,
                ObraId: requisicion.ObraId,
                SolicitanteId: requisicion.SolicitanteId,
                AprobadorId: aprobadorId,
                Items: (requisicion.Items ?? Enumerable.Empty<RequisicionItem>())
                    .Select(i => i.ToAprobadaItemEvent())
                    .ToList(),
                Etapa: requisicion.Etapa);
        }

        public static RequisicionAprobadaItemEvent ToAprobadaItemEvent(this RequisicionItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            return new RequisicionAprobadaItemEvent(
                item.ArticuloId,
                item.Descripcion,
                item.Cantidad,
                item.UnidadMedida);
        }

        public static CreateRequisicionCommand ToCreateCommand(
            this CrearRequisicionRequestDto request,
            Guid solicitanteId,
            string? idempotencyKey = null,
            bool validarObraAsignada = false,
            string? authorizationHeader = null,
            string? correlationId = null)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new CreateRequisicionCommand(
                request.ObraId,
                solicitanteId,
                request.Items,
                request.Etapa,
                request.Observaciones,
                idempotencyKey,
                validarObraAsignada,
                authorizationHeader,
                correlationId);
        }

        public static ReenviarRequisicionCommand ToReenviarCommand(
            this EditarRequisicionRequestDto request,
            Guid requisicionId,
            Guid usuarioId,
            bool esAdmin)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new ReenviarRequisicionCommand(
                requisicionId,
                usuarioId,
                esAdmin,
                request.Items,
                request.Etapa,
                request.Observaciones);
        }

        public static RechazarRequisicionCommand ToRechazarCommand(
            this RechazarRequisicionRequestDto request,
            Guid requisicionId,
            Guid rechazadorId)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new RechazarRequisicionCommand(
                requisicionId,
                rechazadorId,
                request.Motivo ?? string.Empty,
                request.CategoriaMotivo,
                request.Comentario);
        }
    }
}
