using MediatR;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public record CotizarConMagicLinkCommand(
    string Hash,
    List<CotizacionItemDto> Items,
    decimal Flete,
    int PlazoEntregaDias
) : IRequest<CotizacionResponseDto>;

public record CotizacionItemDto(
    Guid LicitacionItemId = default,
    decimal PrecioUnitario = 0m,
    Guid ArticuloId = default)
{
    public Guid IdentificadorEfectivo => ArticuloId != Guid.Empty ? ArticuloId : LicitacionItemId;
}
