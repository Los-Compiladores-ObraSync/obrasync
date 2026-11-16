using MediatR;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

public record GenerarMagicLinkCommand(Guid LicitacionId, Guid ProveedorId) : IRequest<string>;