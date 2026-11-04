namespace ObraSync.Licitaciones.Application.DTOs;

public class PliegoItemDto
{
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
}
