using MediatR;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

public record GenerarMagicLinkCommand(Guid LicitacionId, Guid ProveedorId) : IRequest<GenerarMagicLinkResponseDto>;

public record GenerarMagicLinkResponseDto(
    string Token,
    string Hash,
    string Url,
    DateTime FechaDeVencimiento,
    string ExpiresIn,
    double HorasVigencia);
