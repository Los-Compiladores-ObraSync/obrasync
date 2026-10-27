namespace ObraSync.Licitaciones.Domain.Scoring;

/// <summary>Un ítem del pliego: qué artículo y cuánta cantidad se necesita.</summary>
public record ItemRequerido(Guid ArticuloId, decimal Cantidad);

/// <summary>Precio ofertado por un proveedor para un artículo.</summary>
public record OfertaItem(Guid ArticuloId, decimal PrecioUnitario);

/// <summary>Una cotización lista para puntuar. Calificacion es opcional.</summary>
public record Oferta(
    Guid CotizacionId,
    Guid ProveedorId,
    IReadOnlyList<OfertaItem> Items,
    decimal Flete,
    int PlazoEntregaDias,
    decimal? Calificacion = null);

public record ResultadoScoring(Guid CotizacionId, Guid ProveedorId, decimal PrecioTotal, decimal Puntaje);