namespace ObraSync.Licitaciones.Domain.Entities;

public class RequisicionAprobadaItem
{
    public Guid Id { get; set; }
    public Guid RequisicionId { get; set; }
    public Guid ArticuloId { get; set; }
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    public string UnidadMedida { get; set; } = "";
}
