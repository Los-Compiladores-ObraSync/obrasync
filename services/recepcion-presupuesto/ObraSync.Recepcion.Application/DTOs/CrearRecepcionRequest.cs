using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.DTOs;

public class CrearRecepcionRequest
{
    public Guid ObraId { get; set; }
    public Guid OrdenCompraId { get; set; }
    public string NumeroRemito { get; set; } = string.Empty;
    public DateTime FechaRecepcion { get; set; }
    public EstadoRecepcion? Estado { get; set; }
    public string? MotivoProblema { get; set; }
    public string? Observaciones { get; set; }
    public List<CrearDetalleRecepcionRequest> Detalles { get; set; } = new();
}
