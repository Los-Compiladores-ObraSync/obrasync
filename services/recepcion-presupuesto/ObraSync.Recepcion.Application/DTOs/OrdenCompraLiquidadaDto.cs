namespace ObraSync.Recepcion.Application.DTOs;

/// <summary>
/// Resultado de liquidar administrativamente una orden de compra sin saldo pendiente (#92).
/// </summary>
public class OrdenCompraLiquidadaDto
{
    public Guid OrdenCompraId { get; set; }
    public Guid ObraId { get; set; }
    public decimal MontoLiquidado { get; set; }
    public Guid LiquidadaPorUsuarioId { get; set; }
    public DateTime LiquidadaEn { get; set; }
}
