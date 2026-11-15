using MediatR;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public class CotizarConMagicLinkCommandHandler : IRequestHandler<CotizarConMagicLinkCommand, Unit>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly ICotizacionRepository _cotizacionRepository;
    private readonly IUnitOfWork _unitOfWork; // O tu interfaz para persistir cambios si la usan

    public CotizarConMagicLinkCommandHandler(
        IMagicLinkRepository magicLinkRepository,
        ICotizacionRepository cotizacionRepository)
    {
        _magicLinkRepository = magicLinkRepository;
        _cotizacionRepository = cotizacionRepository;
    }

    public async Task<Unit> Handle(CotizarConMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Buscar el Magic Link por su hash
        var magicLink = await _magicLinkRepository.GetByHashAsync(request.Hash, cancellationToken);

        // 2. Validar existencia, vencimiento y uso previo
        if (magicLink == null || magicLink.FechaDeVencimiento < DateTime.UtcNow || magicLink.FechaDeUso != null)
        {
            throw new InvalidOperationException("El enlace de cotización es inválido, ya expiró o ya fue utilizado.");
        }

        // 3. Marcar el enlace como usado (al estar trackeado por EF, se persiste al guardar cambios)
        magicLink.FechaDeUso = DateTime.UtcNow;

        // 4. Crear la cotización usando las propiedades correctas de tu entidad Domain
        var cotizacion = new Cotizacion
        {
            LicitacionId = magicLink.LicitacionId,
            ProveedorId = magicLink.ProveedorId,
            FechaCreacion = DateTime.UtcNow,
            Flete = request.Flete,
            PlazoEntregaDias = request.PlazoEntregaDias
            // Si requiere agregar los ítems, mapealos según tu entidad de dominio
        };

        // 5. Agregar la cotización mediante su repositorio
        await _cotizacionRepository.AddAsync(cotizacion, cancellationToken);

        return Unit.Value;
    }
}