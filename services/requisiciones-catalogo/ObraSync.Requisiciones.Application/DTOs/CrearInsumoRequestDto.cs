using ObraSync.Requisiciones.Domain.Constants;

namespace ObraSync.Requisiciones.Application.DTOs
{
    public class CrearInsumoRequestDto
    {
        public string Codigo { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string UnidadMedida { get; set; } = string.Empty;
        public string Moneda { get; set; } = Monedas.PesoArgentino;
        public decimal? PrecioReferencia { get; set; }
        public bool Activo { get; set; } = true;
    }
}
