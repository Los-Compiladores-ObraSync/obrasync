namespace ObraSync.Recepcion.Api.DTOs;

public class CrearDetalleRecepcionRequest
{
    public Guid ArticuloId { get; set; }
    public string DescripcionArticulo { get; set; } = string.Empty;
    public decimal CantidadEsperada { get; set; }
    public decimal CantidadRecibida { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public bool Conforme { get; set; }
    public string? Observacion { get; set; }
}
