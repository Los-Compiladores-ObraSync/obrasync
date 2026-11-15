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

        // Crear la entidad usando inicializador de objetos
        var magicLink = new MagicLink
        {
            Id = Guid.NewGuid(),
            Hash = hash,
            LicitacionId = request.LicitacionId,
            ProveedorId = request.ProveedorId,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false
        };

        await _magicLinkRepository.AddAsync(magicLink, cancellationToken);

        return hash;
    }
}