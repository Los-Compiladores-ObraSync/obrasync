namespace ObraSync.Licitaciones.Application.DTOs;

/// <summary>
/// El último precio unitario que se pagó (en una orden de compra emitida) por un artículo.
/// Sirve para comparar las ofertas nuevas contra lo que ya se pagó antes.
/// </summary>
public record PrecioHistoricoDto(Guid ArticuloId, decimal UltimoPrecioUnitario, DateTime FechaEmision);
