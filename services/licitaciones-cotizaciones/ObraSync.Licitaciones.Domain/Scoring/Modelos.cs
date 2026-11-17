namespace ObraSync.Licitaciones.Domain.Scoring;

/// <summary>Un ítem del pliego: qué artículo y cuánta cantidad se necesita.</summary>
public record ItemRequerido(Guid ArticuloId, decimal Cantidad);

/// <summary>Precio ofertado por un proveedor para un artículo (y cantidad ofrecida opcional cuando cotiza presentación alternativa, #190).</summary>
public record OfertaItem(Guid ArticuloId, decimal PrecioUnitario, decimal? CantidadOfrecida = null);

/// <summary>Una cotización lista para puntuar. Calificacion es opcional.</summary>
public record Oferta(
    Guid CotizacionId,
    Guid ProveedorId,
    IReadOnlyList<OfertaItem> Items,
    decimal Flete,
    int PlazoEntregaDias,
    decimal? Calificacion = null);

/// <summary>
/// Cuánto aportó cada criterio al puntaje de una oferta (0 a 1): peso × valor normalizado ÷ suma de pesos.
/// Los cuatro suman exactamente el Puntaje (#206).
/// </summary>
public record AporteScoring(decimal Precio, decimal Flete, decimal Plazo, decimal Calificacion);

public record ResultadoScoring(Guid CotizacionId, Guid ProveedorId, decimal PrecioTotal, decimal Puntaje, AporteScoring? Aporte = null);