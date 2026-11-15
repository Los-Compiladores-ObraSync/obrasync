namespace ObraSync.Recepcion.Application.DTOs;

public class OrdenPendienteDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public List<ItemOrdenPendienteDto> Items { get; set; } = new();
}
