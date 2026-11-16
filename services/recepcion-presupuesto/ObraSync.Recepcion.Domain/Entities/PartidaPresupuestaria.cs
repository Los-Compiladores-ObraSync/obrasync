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

    // Control presupuestario en 3 etapas (#154): asignado -> comprometido (OC emitida) -> ejecutado (recepcionado)
    public decimal MontoAsignado { get; set; }
    public decimal MontoComprometido { get; set; }
    public decimal MontoEjecutado { get; set; }

    public string Moneda { get; set; } = "ARS";

    // Token de concurrencia optimista (#195): evita que dos órdenes simultáneas
    // lean el mismo MontoComprometido y superen la partida sin bloqueo.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Saldo disponible que todavía no fue comprometido por ninguna OC (#154)
    public decimal MontoDisponible => MontoAsignado - MontoComprometido;

    /// <summary>
    /// Evalúa si el monto de una nueva OC provocaría un desvío presupuestario (#94, #154).
    /// Regla pura: (MontoComprometido + MontoOC) > MontoAsignado.
    /// </summary>
    public bool SuperaPartida(decimal montoNuevaOc)
    {
        if (montoNuevaOc < 0)
            throw new ArgumentOutOfRangeException(nameof(montoNuevaOc), "El monto de la orden de compra no puede ser negativo.");

        return (MontoComprometido + montoNuevaOc) > MontoAsignado;
    }

    /// <summary>
    /// Compromete presupuesto al emitirse una orden de compra no bloqueada (#154, #95).
    /// </summary>
    public void ComprometerMonto(decimal monto)
    {
        if (monto < 0)
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto a comprometer no puede ser negativo.");

        MontoComprometido += monto;
    }

    /// <summary>
    /// Registra el gasto real devengado al confirmarse la recepción física de mercadería (#154).
    /// </summary>
    public void EjecutarMonto(decimal monto)
    {
        if (monto < 0)
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto ejecutado no puede ser negativo.");

        MontoEjecutado += monto;
    }

    /// <summary>
    /// Libera monto comprometido ante cancelaciones o ajustes de remanentes (#154).
    /// </summary>
    public void LiberarComprometido(decimal monto)
    {
        if (monto < 0)
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto a liberar no puede ser negativo.");

        MontoComprometido = Math.Max(0, MontoComprometido - monto);
    }
}
