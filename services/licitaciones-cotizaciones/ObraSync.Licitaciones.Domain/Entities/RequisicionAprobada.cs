namespace ObraSync.Licitaciones.Domain.Entities;

public class RequisicionAprobada
{
    public Guid RequisicionId { get; set; }            // clave: el mismo Id que manda Requisiciones
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public string? Etapa { get; set; }                 // texto opcional que manda Requisiciones (#107)
    public DateTime RecibidaEnUtc { get; set; }
    public List<RequisicionAprobadaItem> Items { get; set; } = new();
}
