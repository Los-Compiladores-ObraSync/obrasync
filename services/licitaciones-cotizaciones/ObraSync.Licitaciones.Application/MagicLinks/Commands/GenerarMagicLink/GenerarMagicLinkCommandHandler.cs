using MediatR;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

public class GenerarMagicLinkCommandHandler : IRequestHandler<GenerarMagicLinkCommand, string>
{
    private readonly IMagicLinkRepository _magicLinkRepository;

    public GenerarMagicLinkCommandHandler(IMagicLinkRepository magicLinkRepository)
    {
        _magicLinkRepository = magicLinkRepository;
    }

    public async Task<string> Handle(GenerarMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // Generar un hash único y seguro
        var hash = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_").Replace("+", "-").TrimEnd('=');

        // Crear la entidad con vencimiento a 1 hora (según requerimiento #86)
        var magicLink = new MagicLink(hash, request.LicitacionId, request.ProveedorId, TimeSpan.FromHours(1));

        await _magicLinkRepository.AddAsync(magicLink, cancellationToken);

        return hash;
    }
}