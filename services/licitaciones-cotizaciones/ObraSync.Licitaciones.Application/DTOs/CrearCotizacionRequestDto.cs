namespace ObraSync.Licitaciones.Application.DTOs;

public class CrearCotizacionRequestDto
{
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public decimal Flete { get; set; }
    public int PlazoEntregaDias { get; set; }
    public List<CrearCotizacionItemDto> Items { get; set; } = new();
}
