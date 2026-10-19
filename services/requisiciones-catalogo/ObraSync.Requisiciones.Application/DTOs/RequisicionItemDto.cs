using System;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class RequisicionItemDto
    {
        public Guid ArticuloId { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public decimal Cantidad { get; set; }
        public string UnidadMedida { get; set; } = string.Empty;
    }
}
