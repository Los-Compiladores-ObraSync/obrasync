using System.ComponentModel.DataAnnotations;

namespace ObraSync.Licitaciones.Domain.Entities;

public class Licitacion
{
    public Guid LicitacionId { get; set; }
    public Guid RequisicionId { get; set; }             // GUID, sin clave foránea
    public string NumeroRequisicion { get; set; } = "";
    public Guid ObraId { get; set; }
    public EstadoLicitacion Estado { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime FechaLimiteCotizacion { get; set; }
    public string? MotivoCierre { get; set; }
    public DateTime? FechaCierre { get; set; }
    public DateTime? FechaAdjudicacion { get; set; }
    public List<LicitacionItem> Items { get; set; } = new();

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
