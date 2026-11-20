using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.Services;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public class CotizarConMagicLinkCommandHandler : IRequestHandler<CotizarConMagicLinkCommand, CotizacionResponseDto>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly ICotizacionValidadorService _validadorService;

    public CotizarConMagicLinkCommandHandler(
        IMagicLinkRepository magicLinkRepository,
        ICotizacionRepository cotizacionRepository,
        ILicitacionRepository licitacionRepository,
        ICotizacionValidadorService? validadorService = null)
    {
        _magicLinkRepository = magicLinkRepository ?? throw new ArgumentNullException(nameof(magicLinkRepository));
        _cotizacionRepository = cotizacionRepository ?? throw new ArgumentNullException(nameof(cotizacionRepository));
        if (licitacionRepository == null) throw new ArgumentNullException(nameof(licitacionRepository));
        _validadorService = validadorService ?? new CotizacionValidadorService(licitacionRepository, cotizacionRepository);
    }

    public async Task<CotizacionResponseDto> Handle(CotizarConMagicLinkCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Hash))
        {
            throw new ArgumentException("El hash del enlace de cotización es obligatorio.");
        }

        // 1. Buscar el Magic Link por el hash SHA-256 del token recibido (o coincidencia directa)
        var magicLink = await _magicLinkRepository.GetByHashAsync(request.Hash, cancellationToken);
        if (magicLink == null)
        {
            throw new KeyNotFoundException("El enlace de cotización no existe o es inválido.");
        }

        // 2. Validar que no haya sido usado previamente y que no esté vencido
        if (magicLink.FechaDeUso != null)
        {
            throw new InvalidOperationException("El enlace de cotización ya fue utilizado.");
        }

        var ahoraUtc = DateTime.UtcNow;
        var fechaVencimientoUtc = DateTime.SpecifyKind(magicLink.FechaDeVencimiento, DateTimeKind.Utc);
        if (fechaVencimientoUtc <= ahoraUtc)
        {
            throw new ArgumentException("El enlace de cotización ya expiró.");
        }

        // 3. Reusar la validación compartida de #71 y #190 ANTES de consumir el enlace:
        // Si los ítems están vacíos, incompletos o inválidos, se rechaza con 400 sin quemar el Magic Link.
        var itemsMapeados = request.Items?
            .Select(i => new CrearCotizacionItemDto
            {
                ArticuloId = i != null ? i.IdentificadorEfectivo : Guid.Empty,
                PrecioUnitario = i?.PrecioUnitario ?? 0m,
                PresentacionOfrecida = i?.PresentacionOfrecida,
                CantidadOfrecida = i?.CantidadOfrecida,
                CoberturaParcial = i?.CoberturaParcial ?? false
            })
            .ToList();

        var (_, cotizacion, dto) = await _validadorService.ValidarYConstruirAsync(
            magicLink.LicitacionId,
            magicLink.ProveedorId,
            request.Flete,
            request.PlazoEntregaDias,
            itemsMapeados,
            cancellationToken,
            magicLink.ProveedorNombre);

        // 4. Consumo atómico del enlace (UPDATE condicional: FechaDeUso == null && FechaDeVencimiento > ahoraUtc)
        var consumido = await _magicLinkRepository.TryConsumirAtomicoAsync(
            magicLink.Id,
            DateTime.UtcNow,
            cancellationToken);

        if (!consumido)
        {
            throw new InvalidOperationException("El enlace de cotización ya fue utilizado o expiró.");
        }

        // 5. Persistir la cotización con todos sus ítems en la base de datos
        try
        {
            await _cotizacionRepository.AddAsync(cotizacion, cancellationToken);
            await _cotizacionRepository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Si la cotización no se guardó, el enlace no puede quedar gastado: el proveedor tiene que poder reintentar.
            // CancellationToken.None a propósito: la liberación tiene que correr aunque el pedido se haya cancelado.
            await _magicLinkRepository.LiberarAsync(magicLink.Id, CancellationToken.None);
            throw;
        }

        return dto;
    }
}
