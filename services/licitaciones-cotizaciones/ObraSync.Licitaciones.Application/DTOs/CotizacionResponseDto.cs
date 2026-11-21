namespace ObraSync.Licitaciones.Application.DTOs;

public class CotizacionResponseDto
{
    public Guid CotizacionId { get; set; }
    public Guid LicitacionId { get; set; }
    public Guid ProveedorId { get; set; }
    public string? ProveedorNombre { get; set; }          // NUEVO (#206)
    public decimal? CalificacionProveedor { get; set; }  // NUEVO (#206) - promedio en escala 1 a 5 (con decimales) o null
    public int CantidadCalificacionesProveedor { get; set; } // NUEVO (#222) - cantidad total de calificaciones del proveedor
    public decimal SubtotalItems { get; set; }
    public decimal Flete { get; set; }
    public decimal CostoTotal { get; set; }
    public bool EsMasBarata { get; set; }
    public bool CoberturaParcial { get; set; }           // NUEVO (#190)
    public int PlazoEntregaDias { get; set; }
    public DateTime FechaCreacion { get; set; }
    public IReadOnlyList<CrearCotizacionItemDto> Items { get; set; } = Array.Empty<CrearCotizacionItemDto>();
    public decimal PuntajeTotal { get; set; }
    public bool EsGanadora { get; set; }
    public CriteriosScoringDto? DesglosePuntaje { get; set; }      // NUEVO (#206)
    public CriteriosScoringDto? PonderacionesScoring { get; set; } // NUEVO (#206)
    public DesgloseScoringDto? DesgloseScoring { get; set; }       // NUEVO (#197)
}
