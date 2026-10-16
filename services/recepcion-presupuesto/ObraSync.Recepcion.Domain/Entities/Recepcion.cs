using ObraSync.Recepcion.Domain.Common;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Domain.Entities;

public class Recepcion : BaseEntity
{
    // Referencia al microservicio de Usuarios y Obras (solo GUID, sin FK de SQL)
    public Guid ObraId { get; set; }

    // Referencia al microservicio de Licitaciones y Cotizaciones (solo GUID, sin FK de SQL)
    public Guid OrdenCompraId { get; set; }

    // Referencia al usuario (Capataz/Receptor) de Usuarios y Obras (solo GUID)
    public Guid RecepcionadoPorUsuarioId { get; set; }

    // Identificador comercial del comprobante de entrega en obra (remito)
    public string NumeroRemito { get; set; } = string.Empty;

    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    public EstadoRecepcion Estado { get; set; } = EstadoRecepcion.Pendiente;

    public string? Observaciones { get; set; }

    // Relación interna (misma base de datos SQL Server: DB_Recepcion)
    public ICollection<DetalleRecepcion> Detalles { get; set; } = new List<DetalleRecepcion>();
}
