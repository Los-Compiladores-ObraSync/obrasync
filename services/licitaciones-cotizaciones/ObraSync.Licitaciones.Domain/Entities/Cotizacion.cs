namespace ObraSync.Licitaciones.Domain.Entities;

public class Cotizacion
{
    public Guid CotizacionId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>Nombre del proveedor tal como figuraba al cotizar (#206). Null si no se conoce: no se inventa.</summary>
    public string? ProveedorNombre { get; set; }
    public decimal Flete { get; set; }
    public int PlazoEntregaDias { get; set; }
    public DateTime FechaCreacion { get; set; }
    public List<CotizacionItem> Items { get; set; } = new();
}
