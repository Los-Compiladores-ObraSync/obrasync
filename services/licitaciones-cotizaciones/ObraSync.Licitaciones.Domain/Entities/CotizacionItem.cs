namespace ObraSync.Licitaciones.Domain.Entities;

public class CotizacionItem
{
    public Guid Id { get; set; }
    public Guid CotizacionId { get; set; }
    public Guid ArticuloId { get; set; }
    public decimal PrecioUnitario { get; set; }
    public string? PresentacionOfrecida { get; set; }
    public decimal? CantidadOfrecida { get; set; }
    public bool CoberturaParcial { get; set; }

    public decimal CalcularSubtotal(decimal cantidadPliego) =>
        (CantidadOfrecida is > 0 ? CantidadOfrecida.Value : cantidadPliego) * PrecioUnitario;
}
