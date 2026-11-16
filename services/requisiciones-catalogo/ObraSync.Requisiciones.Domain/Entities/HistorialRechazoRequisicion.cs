using System;

namespace ObraSync.Requisiciones.Domain.Entities
{
    public class HistorialRechazoRequisicion
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid RequisicionId { get; set; }
        public Guid? RechazadorId { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public DateTime FechaRechazo { get; set; } = DateTime.UtcNow;

        public HistorialRechazoRequisicion() { }

        public HistorialRechazoRequisicion(
            Guid id,
            Guid requisicionId,
            Guid? rechazadorId,
            string motivo,
            DateTime fechaRechazo)
        {
            if (requisicionId == Guid.Empty)
                throw new ArgumentException("El identificador de la requisición es obligatorio.", nameof(requisicionId));

            if (string.IsNullOrWhiteSpace(motivo))
                throw new ArgumentException("El motivo de rechazo es obligatorio.", nameof(motivo));

            Id = id == Guid.Empty ? Guid.NewGuid() : id;
            RequisicionId = requisicionId;
            RechazadorId = rechazadorId;
            Motivo = motivo.Trim();
            FechaRechazo = fechaRechazo.ToUniversalTime();
        }
    }
}
