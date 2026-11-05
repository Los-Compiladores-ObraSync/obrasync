namespace ObraSync.Licitaciones.Application.DTOs;

public class CotizacionResponseDto
{
    public Guid CotizacionId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public decimal Flete { get; set; }
    public int PlazoEntregaDias { get; set; }
    public DateTime FechaCreacion { get; set; }
    public IReadOnlyList<CrearCotizacionItemDto> Items { get; set; } = Array.Empty<CrearCotizacionItemDto>();
    public decimal PuntajeTotal { get; set; }
    public bool EsGanadora { get; set; }
}