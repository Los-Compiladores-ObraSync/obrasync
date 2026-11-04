using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;

public record CrearCotizacionCommand(
    Guid LicitacionId,
    Guid ProveedorId,
    decimal Flete,
    int PlazoEntregaDias,
    IReadOnlyList<CrearCotizacionItemDto> Items);
