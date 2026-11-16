using System.Globalization;
using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Configuration;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

public class GenerarMagicLinkCommandHandler : IRequestHandler<GenerarMagicLinkCommand, string>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly IConfiguration? _configuration;

    public GenerarMagicLinkCommandHandler(
        IMagicLinkRepository magicLinkRepository,
        IConfiguration? configuration = null)
    {
        _magicLinkRepository = magicLinkRepository ?? throw new ArgumentNullException(nameof(magicLinkRepository));
        _configuration = configuration;
    }

    public async Task<string> Handle(GenerarMagicLinkCommand request, CancellationToken cancellationToken)
    {
        if (request.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la licitación es obligatorio.");
        }

        if (request.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.");
        }

        double horasVigencia = 1.0;
        var configValue = _configuration?["MagicLink:HorasVigencia"];
        if (!string.IsNullOrWhiteSpace(configValue) &&
            double.TryParse(configValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedHoras) &&
            parsedHoras > 0)
        {
            horasVigencia = parsedHoras;
        }

        // Generar un token aleatorio criptográficamente seguro y guardar su hash SHA-256
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');

        var hashAlmacenado = MagicLinkHashHelper.ComputeSha256(rawToken);

        var magicLink = new MagicLink
        {
            Id = Guid.NewGuid(),
            Hash = hashAlmacenado,
            LicitacionId = request.LicitacionId,
            ProveedorId = request.ProveedorId,
            FechaDeVencimiento = DateTime.UtcNow.AddHours(horasVigencia),
            FechaDeUso = null
        };

        await _magicLinkRepository.AddAsync(magicLink, cancellationToken);

        return rawToken;
    }
}
