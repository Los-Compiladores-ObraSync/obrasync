namespace ObraSync.Licitaciones.Application.DTOs;

/// <summary>
/// Qué pasó con una requisición aprobada del lado de Licitaciones: si ya hay licitación, cuántas ofertas
/// llegaron y qué órdenes de compra salieron. La recepción en obra la completa el frontend con el
/// servicio de Recepción (cada servicio conoce solo lo suyo).
/// </summary>
public record SeguimientoRequisicionDto(
    Guid RequisicionId,
    Guid? LicitacionId,
    string? EstadoLicitacion,
    DateTime? FechaLimiteCotizacion,
    int CantidadCotizaciones,
    IReadOnlyList<SeguimientoOrdenCompraDto> OrdenesCompra);

public record SeguimientoOrdenCompraDto(
    Guid OrdenCompraId,
    string Estado,
    DateTime FechaEmision,
    DateTime? FechaDespacho,
    Guid ProveedorId,
    string? ProveedorNombre,
    Guid? DespachadaPor = null,
    DateTime? DespachadaEn = null,
    DateTime? FechaEstimadaLlegada = null,
    string? Observacion = null,
    bool RecibidaCompleta = false,
    DateTime? FechaRecepcionCompleta = null);
