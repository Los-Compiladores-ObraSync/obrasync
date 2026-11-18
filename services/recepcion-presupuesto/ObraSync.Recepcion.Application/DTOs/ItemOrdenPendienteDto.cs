namespace ObraSync.Recepcion.Application.DTOs;

public class ItemOrdenPendienteDto
{
    public Guid ArticuloId { get; set; }
    public string DescripcionArticulo { get; set; } = string.Empty;
    public decimal CantidadEsperada { get; set; }
    public decimal CantidadRecibida { get; set; }
    public decimal CantidadPendiente { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
}
