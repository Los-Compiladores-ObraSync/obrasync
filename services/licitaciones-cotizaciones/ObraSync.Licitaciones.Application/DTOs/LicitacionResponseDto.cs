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

    /// <summary>
    /// Solo se completa cuando quien consulta es un Proveedor: true si ya cotizó este pliego.
    /// Para Comprador y Admin queda en null (no aplica).
    /// </summary>
    public bool? YaCotizada { get; set; }
}
