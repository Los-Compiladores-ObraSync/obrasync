using System;
using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Domain.Presupuestos;

/// <summary>
/// Lógica pura de evaluación de desvío presupuestario (#94, #154).
/// Regla de negocio: Si Gasto Acumulado (MontoComprometido) + Monto de la OC > Partida de la etapa (MontoAsignado),
/// la orden de compra debe bloquearse preventivamente (#95).
/// </summary>
public static class EvaluadorDesvioPresupuestario
{
    /// <summary>
    /// Evalúa de forma pura si la orden de compra debe bloquearse (#94).
    /// </summary>
    /// <param name="partida">Partida presupuestaria asignada a la etapa u obra (puede ser null si no está definida).</param>
    /// <param name="montoOrdenCompra">Monto total de la orden de compra emitida (incluye flete, #74).</param>
    /// <returns>True si la orden de compra debe bloquearse por superar la partida; de lo contrario, False.</returns>
    public static bool DebeBloquearse(PartidaPresupuestaria? partida, decimal montoOrdenCompra)
    {
        return Evaluar(partida, montoOrdenCompra).DebeBloquearse;
    }

    /// <summary>
    /// Evalúa la regla de desvío presupuestario y retorna el resultado detallado con métricas y motivo descriptivo.
    /// </summary>
    /// <param name="partida">Partida presupuestaria asignada a la etapa u obra.</param>
    /// <param name="montoOrdenCompra">Monto total de la orden de compra emitida (incluye flete, #74).</param>
    /// <returns>ResultadoEvaluacionDesvio con detalle numérico y justificación de negocio.</returns>
    public static ResultadoEvaluacionDesvio Evaluar(PartidaPresupuestaria? partida, decimal montoOrdenCompra)
    {
        if (montoOrdenCompra < 0)
            throw new ArgumentOutOfRangeException(nameof(montoOrdenCompra), "El monto de la orden de compra no puede ser negativo.");

        // Decisión #154: si no hay partida cargada para la etapa, no se bloquea preventivamente la operación
        if (partida is null)
        {
            return new ResultadoEvaluacionDesvio(
                DebeBloquearse: false,
                MontoAsignado: 0m,
                GastoAcumulado: 0m,
                MontoOrdenCompra: montoOrdenCompra,
                MontoProyectado: montoOrdenCompra,
                Exceso: 0m,
                Motivo: "No existe partida presupuestaria definida para la etapa; no se bloquea preventivamente (#154).");
        }

        // Decisión #154: el gasto acumulado es el MontoComprometido por órdenes de compra previas emitidas
        var gastoAcumulado = partida.MontoComprometido;
        var montoProyectado = gastoAcumulado + montoOrdenCompra;

        // Regla pura: (MontoComprometido + MontoOC) > MontoAsignado. La igualdad exacta no desvía (#94).
        var desvia = montoProyectado > partida.MontoAsignado;
        var exceso = desvia ? (montoProyectado - partida.MontoAsignado) : 0m;

        string motivo;
        if (desvia)
        {
            motivo = $"El monto proyectado ({montoProyectado:N2}) supera la partida asignada ({partida.MontoAsignado:N2}) por un exceso de {exceso:N2}. La orden de compra debe bloquearse (#94, #95).";
        }
        else if (montoProyectado == partida.MontoAsignado)
        {
            motivo = $"El monto proyectado ({montoProyectado:N2}) coincide exactamente con la partida asignada ({partida.MontoAsignado:N2}); no supera el tope permitido (#94).";
        }
        else
        {
            motivo = $"El monto proyectado ({montoProyectado:N2}) se encuentra dentro del margen disponible de la partida asignada ({partida.MontoAsignado:N2}).";
        }

        return new ResultadoEvaluacionDesvio(
            DebeBloquearse: desvia,
            MontoAsignado: partida.MontoAsignado,
            GastoAcumulado: gastoAcumulado,
            MontoOrdenCompra: montoOrdenCompra,
            MontoProyectado: montoProyectado,
            Exceso: exceso,
            Motivo: motivo);
    }
}
