namespace ObraSync.Recepcion.Infrastructure.Configuration;

/// <summary>
/// Configuración del presupuesto semilla (sección "Presupuesto"; en Docker: Presupuesto__MontoSemillaPorEtapa).
/// Con 0 (o sin definir) no se crean partidas solas y todo sigue como antes: las órdenes sin partida se marcan
/// SinPartidaPresupuestaria. El monto lo define el equipo; el código no trae un valor propio.
/// </summary>
public class PresupuestoSemillaOptions
{
    public const string Seccion = "Presupuesto";

    /// <summary>Monto asignado a cada partida semilla (una por obra y etapa), en ARS.</summary>
    public decimal MontoSemillaPorEtapa { get; set; }
}
