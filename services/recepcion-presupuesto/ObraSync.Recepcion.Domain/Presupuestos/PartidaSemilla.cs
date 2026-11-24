using ObraSync.Recepcion.Domain.Entities;

namespace ObraSync.Recepcion.Domain.Presupuestos;

/// <summary>
/// Partida presupuestaria "semilla": la que se crea sola la primera vez que llega una orden de compra de una
/// obra y etapa que todavía no tiene partida. Así toda obra arranca con un presupuesto y el control
/// preventivo (bloqueo de órdenes que superan la partida) deja de ser decorativo.
/// </summary>
public static class PartidaSemilla
{
    public const string PrefijoCodigo = "SEMILLA-";

    public static PartidaPresupuestaria Crear(Guid obraId, string? etapa, decimal montoAsignado)
    {
        if (obraId == Guid.Empty)
            throw new ArgumentException("El identificador de la obra es obligatorio.", nameof(obraId));

        if (montoAsignado <= 0)
            throw new ArgumentOutOfRangeException(nameof(montoAsignado), "El monto semilla debe ser mayor que cero.");

        var etapaLimpia = string.IsNullOrWhiteSpace(etapa) ? null : etapa.Trim();

        return new PartidaPresupuestaria
        {
            Id = Guid.NewGuid(),
            ObraId = obraId,
            Etapa = etapaLimpia,
            // El código es único por obra: se completa con un sufijo aleatorio corto.
            CodigoPartida = PrefijoCodigo + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Descripcion = etapaLimpia is null
                ? "Presupuesto semilla de la obra"
                : $"Presupuesto semilla de la etapa {etapaLimpia}",
            MontoAsignado = montoAsignado,
            MontoComprometido = 0m,
            MontoEjecutado = 0m,
            Moneda = "ARS",
            CreatedAt = DateTime.UtcNow
        };
    }
}
