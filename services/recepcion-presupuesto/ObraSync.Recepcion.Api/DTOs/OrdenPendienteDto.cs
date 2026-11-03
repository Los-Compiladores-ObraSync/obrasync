namespace ObraSync.Recepcion.Api.DTOs;

public class OrdenPendienteDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public List<ItemOrdenPendienteDto> Items { get; set; } = new();
}

