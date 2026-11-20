using MediatR;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Domain.Entities;
using ObraSync.Licitaciones.Application.DTOs;

namespace ObraSync.Licitaciones.Application.MagicLinks.Queries.ObtenerPliegoPorMagicLink;

public class ObtenerPliegoPorMagicLinkQueryHandler
    : IRequestHandler<ObtenerPliegoPorMagicLinkQuery, PliegoMagicLinkResponseDto>
{
    private readonly IMagicLinkRepository _magicLinkRepository;
    private readonly ILicitacionRepository _licitacionRepository;

    public ObtenerPliegoPorMagicLinkQueryHandler(
        IMagicLinkRepository magicLinkRepository,
        ILicitacionRepository licitacionRepository)
    {
        _magicLinkRepository = magicLinkRepository ?? throw new ArgumentNullException(nameof(magicLinkRepository));
        _licitacionRepository = licitacionRepository ?? throw new ArgumentNullException(nameof(licitacionRepository));
    }

    public async Task<PliegoMagicLinkResponseDto> Handle(
        ObtenerPliegoPorMagicLinkQuery request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Hash))
        {
            throw new ArgumentException("El hash del enlace de cotización es obligatorio.");
        }

        // Mismas reglas y mismos errores que cotizar con el enlace, para que la pantalla muestre lo mismo en los dos pasos.
        // Esta consulta NO consume el enlace: solo lo mira.
        var magicLink = await _magicLinkRepository.GetByHashAsync(request.Hash, cancellationToken);
        if (magicLink == null)
        {
            throw new KeyNotFoundException("El enlace de cotización no existe o es inválido.");
        }

        if (magicLink.FechaDeUso != null)
        {
            throw new InvalidOperationException("El enlace de cotización ya fue utilizado.");
        }

        var fechaVencimientoUtc = DateTime.SpecifyKind(magicLink.FechaDeVencimiento, DateTimeKind.Utc);
        if (fechaVencimientoUtc <= DateTime.UtcNow)
        {
            throw new ArgumentException("El enlace de cotización ya expiró.");
        }

        var licitacion = await _licitacionRepository.GetByIdAsync(magicLink.LicitacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la licitación con Id '{magicLink.LicitacionId}'.");

        if (licitacion.Estado != EstadoLicitacion.Abierta)
        {
            throw new ArgumentException(
                $"No se pueden recibir cotizaciones porque la licitación se encuentra en estado '{licitacion.Estado}'.");
        }

        var fechaLimiteUtc = DateTime.SpecifyKind(licitacion.FechaLimiteCotizacion, DateTimeKind.Utc);
        if (DateTime.UtcNow > fechaLimiteUtc)
        {
            throw new ArgumentException("El plazo límite para cotizar esta licitación ya venció.");
        }

        return new PliegoMagicLinkResponseDto
        {
            LicitacionId = licitacion.LicitacionId,
            NumeroRequisicion = licitacion.NumeroRequisicion,
            FechaLimiteCotizacion = fechaLimiteUtc,
            FechaDeVencimientoEnlace = fechaVencimientoUtc,
            ProveedorNombre = magicLink.ProveedorNombre,
            Items = licitacion.Items
                .Select(i => new PliegoItemDto
                {
                    ArticuloId = i.ArticuloId,
                    Descripcion = i.Descripcion,
                    Cantidad = i.Cantidad,
                    UnidadMedida = i.UnidadMedida
                })
                .ToList()
        };
    }
}
