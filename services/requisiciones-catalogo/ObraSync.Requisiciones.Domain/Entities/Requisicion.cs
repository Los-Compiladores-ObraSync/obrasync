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
            DateTime? fechaCreacion = null)
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

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            NumeroRequisicion = numeroRequisicion.Trim().ToUpperInvariant();
            ObraId = obraId;
            Etapa = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();
            SolicitanteId = solicitanteId;
            Estado = EstadoRequisicion.Pendiente;
            Observaciones = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            FechaCreacion = fechaCreacion?.ToUniversalTime() ?? DateTime.UtcNow;

            foreach (var item in listaItems)
            {
                item.RequisicionId = Id;
                Items.Add(item);
            }
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
