using System;
using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class InsumoCatalogoDto
    {
        public Guid ArticuloId { get; set; }
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public string Moneda { get; set; } = Monedas.PesoArgentino;
        public decimal? PrecioReferencia { get; set; }
        public bool Activo { get; set; }
    }
}
