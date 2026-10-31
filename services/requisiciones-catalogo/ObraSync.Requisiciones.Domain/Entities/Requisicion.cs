using System;
using System.Collections.Generic;
using System.Linq;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Domain.Entities
{
    public class Requisicion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string NumeroRequisicion { get; set; } = string.Empty;
        public Guid ObraId { get; set; }
        public string? Etapa { get; set; }
        public Guid SolicitanteId { get; set; }
        public Guid? AprobadorId { get; set; }
        public string Estado { get; set; } = EstadoRequisicion.Pendiente;
        public string? Observaciones { get; set; }
        public string? IdempotencyKey { get; set; }
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime? FechaAprobacion { get; set; }
        public List<RequisicionItem> Items { get; set; } = new();

        public Requisicion() { }

        public Requisicion(
            Guid id,
            string numeroRequisicion,
            Guid obraId,
            Guid solicitanteId,
            IEnumerable<RequisicionItem> items,
            string? etapa = null,
            string? observaciones = null,
            DateTime? fechaCreacion = null,
            string? idempotencyKey = null)
        {
            if (string.IsNullOrWhiteSpace(numeroRequisicion))
                throw new ArgumentException("El número de requisición no puede estar vacío.", nameof(numeroRequisicion));

            if (obraId == Guid.Empty)
                throw new ArgumentException("El identificador de la obra (ObraId) es obligatorio.", nameof(obraId));

            if (solicitanteId == Guid.Empty)
                throw new ArgumentException("El identificador del solicitante (SolicitanteId) es obligatorio.", nameof(solicitanteId));

            var listaItems = items?.ToList() ?? new List<RequisicionItem>();
            if (listaItems.Count == 0)
                throw new ArgumentException("La requisición debe contener al menos un ítem.", nameof(items));

            var keyNormalizada = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            if (keyNormalizada != null && keyNormalizada.Length > 100)
                throw new ArgumentException("El encabezado Idempotency-Key no puede superar los 100 caracteres.", nameof(idempotencyKey));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            NumeroRequisicion = numeroRequisicion.Trim().ToUpperInvariant();
            ObraId = obraId;
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
            SolicitanteId = solicitanteId;
            Estado = EstadoRequisicion.Pendiente;
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            IdempotencyKey = keyNormalizada;
            FechaCreacion = fechaCreacion?.ToUniversalTime() ?? DateTime.UtcNow;

            foreach (var item in listaItems)
            {
                item.RequisicionId = Id;
                Items.Add(item);
            }
        }

        public bool CoincideConSolicitud(
            Guid solicitanteId,
            Guid obraId,
            string? etapa,
            IEnumerable<(Guid ArticuloId, decimal Cantidad)> itemsSolicitados)
        {
            if (SolicitanteId != solicitanteId || ObraId != obraId)
                return false;

            var etapaEsta = string.IsNullOrWhiteSpace(Etapa) ? null : Etapa.Trim();
            var etapaOtra = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();

            if (!string.Equals(etapaEsta, etapaOtra, StringComparison.OrdinalIgnoreCase))
                return false;

            var listaSolicitada = itemsSolicitados?.ToList() ?? new List<(Guid ArticuloId, decimal Cantidad)>();
            if (Items.Count != listaSolicitada.Count)
                return false;

            var actualesOrdenados = Items
                .OrderBy(i => i.ArticuloId)
                .ThenBy(i => i.Cantidad)
                .Select(i => (i.ArticuloId, i.Cantidad))
                .ToList();

            var solicitadosOrdenados = listaSolicitada
                .OrderBy(i => i.ArticuloId)
                .ThenBy(i => i.Cantidad)
                .ToList();

            for (var i = 0; i < actualesOrdenados.Count; i++)
            {
                if (actualesOrdenados[i].ArticuloId != solicitadosOrdenados[i].ArticuloId ||
                    actualesOrdenados[i].Cantidad != solicitadosOrdenados[i].Cantidad)
                {
                    return false;
                }
            }

            return true;
        }

        public void Aprobar(Guid aprobadorId, DateTime? fechaAprobacion = null)
        {
            if (aprobadorId == Guid.Empty)
                throw new ArgumentException("El identificador del aprobador es obligatorio.", nameof(aprobadorId));

            if (!string.Equals(Estado, EstadoRequisicion.Pendiente, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"No se puede aprobar una requisición en estado '{Estado}'. Solo se pueden aprobar requisiciones pendientes.");

            Estado = EstadoRequisicion.Aprobada;
            AprobadorId = aprobadorId;
            FechaAprobacion = fechaAprobacion?.ToUniversalTime() ?? DateTime.UtcNow;
        }
    }
}
