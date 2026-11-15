using MediatR;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public record CotizarConMagicLinkCommand(
    string Hash,
    List<CotizacionItemDto> Items,
    decimal Flete,
    int PlazoEntregaDias
) : IRequest<Unit>;

public record CotizacionItemDto(Guid LicitacionItemId, decimal PrecioUnitario);