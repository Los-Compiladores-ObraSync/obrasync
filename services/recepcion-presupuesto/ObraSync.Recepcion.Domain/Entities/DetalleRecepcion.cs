using ObraSync.Recepcion.Domain.Common;

namespace ObraSync.Recepcion.Domain.Entities;

public class DetalleRecepcion : BaseEntity
{
    // Clave foránea interna dentro de DB_Recepcion
    public Guid RecepcionId { get; set; }
    public Recepcion Recepcion { get; set; } = null!;

    // Referencia externa al microservicio de Requisiciones y Catálogo (solo GUID, sin FK de SQL)
    public Guid ArticuloId { get; set; }

    // Snapshot de datos del artículo al momento de la recepción para evitar acoplamiento HTTP constante
    public string DescripcionArticulo { get; set; } = string.Empty;

    public decimal CantidadEsperada { get; set; }
    public decimal CantidadRecibida { get; set; }
    public string UnidadMedida { get; set; } = string.Empty;
    public bool Conforme { get; set; } = true;
    public string? Observacion { get; set; }
}
