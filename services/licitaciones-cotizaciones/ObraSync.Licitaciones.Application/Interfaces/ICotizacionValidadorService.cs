using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.Interfaces;

public interface ICotizacionValidadorService
{
    Task<(Licitacion Licitacion, Cotizacion Cotizacion, CotizacionResponseDto ResponseDto)> ValidarYConstruirAsync(
        Guid licitacionId,
        Guid proveedorId,
        decimal flete,
        int plazoEntregaDias,
        IReadOnlyList<CrearCotizacionItemDto>? items,
        CancellationToken cancellationToken = default,
        string? proveedorNombre = null,
        bool permitirReemplazo = false);
}
