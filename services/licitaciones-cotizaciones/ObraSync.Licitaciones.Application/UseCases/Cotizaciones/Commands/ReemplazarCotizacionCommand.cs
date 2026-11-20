using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;

/// <summary>Corrige una oferta ya enviada mientras la licitación siga abierta y no haya vencido el plazo.</summary>
public record ReemplazarCotizacionCommand(
    Guid CotizacionId,
    Guid UsuarioId,
    bool EsAdmin,
    decimal Flete,
    int PlazoEntregaDias,
    IReadOnlyList<CrearCotizacionItemDto> Items);
