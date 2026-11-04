namespace ObraSync.Licitaciones.Application.DTOs;

public class CrearCotizacionItemDto
{
    public Guid ArticuloId { get; set; }
    public decimal PrecioUnitario { get; set; }
}

public class CotizacionItemDto : CrearCotizacionItemDto
{
}
