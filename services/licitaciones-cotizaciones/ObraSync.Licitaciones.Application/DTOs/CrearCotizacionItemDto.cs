namespace ObraSync.Licitaciones.Application.DTOs;

public class CrearCotizacionItemDto
{
    public Guid ArticuloId { get; set; }
    public decimal PrecioUnitario { get; set; }
    public string? PresentacionOfrecida { get; set; }
    public decimal? CantidadOfrecida { get; set; }
    public decimal SubtotalItem { get; set; }
    public bool CoberturaParcial { get; set; }
}

public class CotizacionItemDto : CrearCotizacionItemDto
{
}
