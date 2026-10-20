using System;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class CrearRequisicionItemDto
    {
        public Guid ArticuloId { get; set; }
        public decimal Cantidad { get; set; }
    }
}
