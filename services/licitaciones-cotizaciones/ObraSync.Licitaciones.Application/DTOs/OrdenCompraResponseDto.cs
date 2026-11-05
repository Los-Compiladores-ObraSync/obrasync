namespace ObraSync.Licitaciones.Application.DTOs;

public record OrdenCompraResponseDto(
    Guid Id,
    Guid LicitacionId,
    Guid CotizacionId,
    string Estado,
    DateTime FechaEmision
);