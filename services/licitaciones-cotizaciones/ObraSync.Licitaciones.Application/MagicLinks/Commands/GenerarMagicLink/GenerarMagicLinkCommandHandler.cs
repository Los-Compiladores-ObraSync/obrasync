using System.Globalization;
using System.Security.Cryptography;
using MediatR;
using Microsoft.Extensions.Configuration;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.GenerarMagicLink;

public class GenerarMagicLinkCommandHandler : IRequestHandler<GenerarMagicLinkCommand, GenerarMagicLinkResponseDto>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly ILicitacionRepository? _licitacionRepository;
    private readonly IConfiguration? _configuration;

    public GenerarMagicLinkCommandHandler(
        IMagicLinkRepository magicLinkRepository,
        ILicitacionRepository? licitacionRepository = null,
        IConfiguration? configuration = null)
    {
        _magicLinkRepository = magicLinkRepository ?? throw new ArgumentNullException(nameof(magicLinkRepository));
        _licitacionRepository = licitacionRepository;
        _configuration = configuration;
    }

    public async Task<GenerarMagicLinkResponseDto> Handle(GenerarMagicLinkCommand request, CancellationToken cancellationToken)
    {
        if (request.LicitacionId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la licitación es obligatorio.");
        }

        if (request.ProveedorId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del proveedor es obligatorio.");
        }

        // 1. Validar que la licitación exista y esté Abierta (y vigente) (#198)
        if (_licitacionRepository != null)
        {
            var licitacion = await _licitacionRepository.GetByIdAsync(request.LicitacionId, cancellationToken);
            if (licitacion == null)
            {
                throw new KeyNotFoundException($"No se encontró la licitación con Id '{request.LicitacionId}'.");
            }

            if (licitacion.Estado != EstadoLicitacion.Abierta)
            {
                throw new InvalidOperationException($"No se puede generar un Magic Link porque la licitación se encuentra en estado '{licitacion.Estado}'. Solo se permiten licitaciones en estado Abierta.");
            }

            var fechaLimiteUtc = DateTime.SpecifyKind(licitacion.FechaLimiteCotizacion, DateTimeKind.Utc);
            if (DateTime.UtcNow >= fechaLimiteUtc)
            {
                throw new InvalidOperationException("No se puede generar un Magic Link porque el plazo límite de cotización de la licitación ya venció.");
            }
        }

        // 2. Vigencia configurable desde MagicLink:HorasVigencia (por defecto 1 hora)
        double horasVigencia = 1.0;
        var configValue = _configuration?["MagicLink:HorasVigencia"];
        if (!string.IsNullOrWhiteSpace(configValue) &&
            double.TryParse(configValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedHoras) &&
            parsedHoras > 0)
        {
            horasVigencia = parsedHoras;
        }

        // 3. Generar token criptográficamente seguro de 32 bytes en Base64 URL-safe y guardar SOLO su SHA-256
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace("/", "_")
            .Replace("+", "-")
            .TrimEnd('=');

        var hashAlmacenado = MagicLinkHashHelper.ComputeSha256(rawToken);
        var fechaDeVencimientoUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(horasVigencia), DateTimeKind.Utc);

        var magicLink = new MagicLink
        {
            Id = Guid.NewGuid(),
            Hash = hashAlmacenado,
            LicitacionId = request.LicitacionId,
            ProveedorId = request.ProveedorId,
            FechaDeVencimiento = fechaDeVencimientoUtc,
            FechaDeUso = null
        };

        await _magicLinkRepository.AddAsync(magicLink, cancellationToken);

        var expiresInTexto = Math.Abs(horasVigencia - 1.0) < 0.0001
            ? "1 hour"
            : $"{horasVigencia.ToString(CultureInfo.InvariantCulture)} hours";

        return new GenerarMagicLinkResponseDto(
            Token: rawToken,
            Hash: rawToken,
            Url: $"/api/licitaciones/magic-links/{rawToken}/cotizar",
            FechaDeVencimiento: fechaDeVencimientoUtc,
            ExpiresIn: expiresInTexto,
            HorasVigencia: horasVigencia);
    }
}
