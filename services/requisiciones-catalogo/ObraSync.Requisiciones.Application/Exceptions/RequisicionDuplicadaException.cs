using System;

namespace ObraSync.Requisiciones.Application.Exceptions
{
    public class RequisicionDuplicadaException : InvalidOperationException
    {
        public string NumeroRequisicionExistente { get; }
        public Guid RequisicionExistenteId { get; }

        public RequisicionDuplicadaException(
            string numeroRequisicionExistente,
            Guid requisicionExistenteId,
            string message)
            : base(message)
        {
            NumeroRequisicionExistente = numeroRequisicionExistente;
            RequisicionExistenteId = requisicionExistenteId;
        }
    }
}
