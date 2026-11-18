namespace ObraSync.Licitaciones.Application.DTOs;

public record OrdenCompraResponseDto(
    Guid Id,
    Guid LicitacionId,
    Guid CotizacionId,
    string Estado,
    DateTime FechaEmision,
    Guid ObraId = default,
    Guid ProveedorId = default,
    bool FueDespachada = false,
    DateTime? FechaDespacho = null
);