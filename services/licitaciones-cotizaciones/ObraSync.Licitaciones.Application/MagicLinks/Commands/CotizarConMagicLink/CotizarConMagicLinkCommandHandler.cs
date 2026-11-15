using MediatR;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public class CotizarConMagicLinkCommandHandler : IRequestHandler<CotizarConMagicLinkCommand, Unit>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly ICotizacionRepository _cotizacionRepository;

    public CotizarConMagicLinkCommandHandler(
        IMagicLinkRepository magicLinkRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _magicLinkRepository = magicLinkRepository;
        _cotizacionRepository = cotizacionRepository;
    }

    public async Task<Unit> Handle(CotizarConMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Buscar el Magic Link por su hash usando el repositorio
        var magicLink = await _magicLinkRepository.GetByHashAsync(request.Hash, cancellationToken);

        // 2. Validar existencia, vencimiento y uso previo
        if (magicLink == null || magicLink.FechaDeVencimiento < DateTime.UtcNow || magicLink.FechaDeUso != null)
        {
            throw new InvalidOperationException("El enlace de cotización es inválido, ya expiró o ya fue utilizado.");
        }

        // 3. Marcar el enlace como usado y actualizarlo
        magicLink.FechaDeUso = DateTime.UtcNow;
        await _magicLinkRepository.UpdateAsync(magicLink, cancellationToken);

        // 4. Crear la cotización extrayendo el ProveedorId del Magic Link (Requerimiento #87)
        var cotizacion = new Cotizacion
        {
            Id = Guid.NewGuid(),
            LicitacionId = magicLink.LicitacionId,
            ProveedorId = magicLink.ProveedorId, 
            FechaCreacion = DateTime.UtcNow,
            Flete = request.Flete,
            PlazoEntregaDias = request.PlazoEntregaDias
        };

        // 5. Guardar la cotización mediante su repositorio
        await _cotizacionRepository.AddAsync(cotizacion, cancellationToken);

        return Unit.Value;
    }
}