using ObraSync.Recepcion.Domain.Common;

namespace ObraSync.Recepcion.Domain.Entities;

public class PartidaPresupuestaria : BaseEntity
{
    // Referencia a la Obra (solo GUID de Usuarios y Obras)
    public Guid ObraId { get; set; }

    // Etapa de la obra a la que corresponde la partida (#107). Nula = partida de toda la obra.
    // Es el mismo texto que viaja en OrdenCompraEmitidaEvent (comparación por texto).
    public string? Etapa { get; set; }

    public string CodigoPartida { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;

    // Control presupuestario en 3 etapas: asignado -> comprometido (OC emitida) -> ejecutado (recepcionado)
    public decimal MontoAsignado { get; set; }
    public decimal MontoComprometido { get; set; }
    public decimal MontoEjecutado { get; set; }

    public string Moneda { get; set; } = "ARS";
}
