namespace ObraSync.Recepcion.Domain.Enums;

/// <summary>
/// Estado de la orden de compra dentro del microservicio de Recepción y Control Presupuestario (#94, #95, #96, #154).
/// </summary>
public enum EstadoOrdenCompra
{
    /// <summary>
    /// La orden de compra no supera la partida presupuestaria (o no tiene tope definido) y está habilitada para recepciones (#154).
    /// </summary>
    Activa = 1,

    /// <summary>
    /// La orden de compra superó la partida presupuestaria (#94) y fue bloqueada automáticamente (#95).
    /// No se permite registrar recepciones físicas contra ella hasta su liberación por un Administrador (#96).
    /// </summary>
    Bloqueada = 2
}
