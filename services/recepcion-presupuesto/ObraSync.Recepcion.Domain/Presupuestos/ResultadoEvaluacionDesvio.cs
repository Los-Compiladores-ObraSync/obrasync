namespace ObraSync.Recepcion.Domain.Presupuestos;

/// <summary>
/// Representa el resultado detallado de evaluar la regla de desvío presupuestario (#94, #154).
/// Provee información completa para el consumidor de eventos (#95), auditoría y logs.
/// </summary>
public sealed record ResultadoEvaluacionDesvio(
    bool DebeBloquearse,
    decimal MontoAsignado,
    decimal GastoAcumulado,
    decimal MontoOrdenCompra,
    decimal MontoProyectado,
    decimal Exceso,
    string Motivo);
