namespace ObraSync.Licitaciones.Domain.Entities;

public class CalificacionProveedor
{
    public const int MaxLongitudComentario = 300;

    public Guid Id { get; set; }
    public Guid ProveedorId { get; set; }
    /// <summary>Orden de compra asociada a esta calificación (#222). Null para calificaciones legacy directas por proveedor (#88).</summary>
    public Guid? OrdenCompraId { get; set; }
    /// <summary>Usuario (sub del JWT) que calificó. Cada usuario tiene una sola calificación por orden (#222) o global (#88).</summary>
    public Guid CalificadorId { get; set; }
    public int Calificacion { get; set; }
    /// <summary>Comentario opcional del calificador sobre el cumplimiento en la orden (máx. 300 caracteres, #222).</summary>
    public string? Comentario { get; set; }
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
}
