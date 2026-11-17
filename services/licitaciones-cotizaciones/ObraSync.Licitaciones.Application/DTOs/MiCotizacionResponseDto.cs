namespace ObraSync.Licitaciones.Application.DTOs;

public class MiCotizacionResponseDto
{
    public Guid CotizacionId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public string NumeroRequisicion { get; set; } = string.Empty;
    public Guid ObraId { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public string EstadoLicitacion { get; set; } = string.Empty;
    public decimal SubtotalItems { get; set; }
    public decimal Flete { get; set; }
    public decimal CostoTotal { get; set; }
    public bool CoberturaParcial { get; set; }
    public int PlazoEntregaDias { get; set; }
    public DateTime FechaCreacion { get; set; }
    public IReadOnlyList<CrearCotizacionItemDto> Items { get; set; } = Array.Empty<CrearCotizacionItemDto>();
    public bool EsGanadora { get; set; }
    public string EstadoResultado { get; set; } = string.Empty;
}
