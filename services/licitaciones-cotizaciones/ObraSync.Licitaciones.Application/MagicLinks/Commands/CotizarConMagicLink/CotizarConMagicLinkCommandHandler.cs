using MediatR;
using Microsoft.EntityFrameworkCore;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.MagicLinks.Commands.CotizarConMagicLink;

public class CotizarConMagicLinkCommandHandler : IRequestHandler<CotizarConMagicLinkCommand, Unit>
{
    private readonly IApplicationDbContext _context;

    public CotizarConMagicLinkCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Unit> Handle(CotizarConMagicLinkCommand request, CancellationToken cancellationToken)
    {
        // 1. Buscar el Magic Link por su hash
        var magicLink = await _context.MagicLinks
            .FirstOrDefaultAsync(m => m.Hash == request.Hash, cancellationToken);

        // 2. Validar existencia, vencimiento y uso previo
        if (magicLink == null || magicLink.FechaDeVencimiento < DateTime.UtcNow || magicLink.FechaDeUso != null)
        {
            throw new InvalidOperationException("El enlace de cotización es inválido, ya expiró o ya fue utilizado.");
        }

        // 3. Marcar el enlace como usado en la misma transacción
        magicLink.FechaDeUso = DateTime.UtcNow;

        // 4. Crear la cotización extrayendo el ProveedorId del Magic Link
        var cotizacion = new Cotizacion
        {
            Id = Guid.NewGuid(),
            LicitacionId = magicLink.LicitacionId,
            ProveedorId = magicLink.ProveedorId, 
            FechaCreacion = DateTime.UtcNow,
            Flete = request.Flete,
            PlazoEntregaDias = request.PlazoEntregaDias
        };

        _context.Cotizaciones.Add(cotizacion);

        // 5. Persistir cambios
        await _context.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}