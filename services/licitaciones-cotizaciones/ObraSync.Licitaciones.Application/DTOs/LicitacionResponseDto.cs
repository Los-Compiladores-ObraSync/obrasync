namespace ObraSync.Licitaciones.Application.DTOs;

public class LicitacionResponseDto
{
    public Guid LicitacionId { get; set; }
    public Guid RequisicionId { get; set; }
    public string NumeroRequisicion { get; set; } = string.Empty;
    public Guid ObraId { get; set; }
    public string Estado { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public IReadOnlyList<PliegoItemDto> Items { get; set; } = Array.Empty<PliegoItemDto>();
}
