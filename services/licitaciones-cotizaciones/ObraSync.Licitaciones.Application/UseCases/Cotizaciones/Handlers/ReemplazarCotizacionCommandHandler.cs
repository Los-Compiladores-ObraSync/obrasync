using MediatR;
using ObraSync.Licitaciones.Application.DTOs;
using ObraSync.Licitaciones.Application.Interfaces;
using ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Commands;
using ObraSync.Licitaciones.Domain.Entities;

namespace ObraSync.Licitaciones.Application.UseCases.Cotizaciones.Handlers;

/// <summary>
/// El proveedor corrige su oferta (precios, flete, plazo) antes del cierre. Se conserva el mismo CotizacionId (nada que
/// apunte a la oferta se rompe) y se reutilizan las MISMAS validaciones que al cotizar: la licitación tiene que estar
/// abierta y dentro del plazo, y los ítems tienen que coincidir con el pliego.
/// </summary>
public class ReemplazarCotizacionCommandHandler
    : IRequestHandler<ReemplazarCotizacionCommand, CotizacionResponseDto>
{
    private readonly ICotizacionRepository _cotizaciones;
    private readonly ICotizacionValidadorService _validador;

    public ReemplazarCotizacionCommandHandler(
        ICotizacionRepository cotizaciones,
        ICotizacionValidadorService validador)
    {
        _cotizaciones = cotizaciones ?? throw new ArgumentNullException(nameof(cotizaciones));
        _validador = validador ?? throw new ArgumentNullException(nameof(validador));
    }

    public async Task<CotizacionResponseDto> Handle(
        ReemplazarCotizacionCommand command,
        CancellationToken cancellationToken = default)
    {
        var existente = await _cotizaciones.GetPropiaParaEditarAsync(command.CotizacionId, cancellationToken)
            ?? throw new KeyNotFoundException($"No se encontró la cotización con Id '{command.CotizacionId}'.");

        if (!command.EsAdmin && existente.ProveedorId != command.UsuarioId)
            throw new UnauthorizedAccessException("Solo el proveedor que envió la cotización (o un administrador) puede corregirla.");

        var (_, nueva, dto) = await _validador.ValidarYConstruirAsync(
            existente.LicitacionId,
            existente.ProveedorId,
            command.Flete,
            command.PlazoEntregaDias,
            command.Items,
            cancellationToken,
            existente.ProveedorNombre,
            permitirReemplazo: true);

        existente.Flete = nueva.Flete;
        existente.PlazoEntregaDias = nueva.PlazoEntregaDias;
        existente.FechaCreacion = DateTime.UtcNow;

        var itemsNuevos = nueva.Items
            .Select(item => new CotizacionItem
            {
                Id = Guid.NewGuid(),
                CotizacionId = existente.CotizacionId,
                ArticuloId = item.ArticuloId,
                PrecioUnitario = item.PrecioUnitario,
                PresentacionOfrecida = item.PresentacionOfrecida,
                CantidadOfrecida = item.CantidadOfrecida,
                CoberturaParcial = item.CoberturaParcial
            })
            .ToList();
        _cotizaciones.ReemplazarItems(existente, itemsNuevos);

        await _cotizaciones.SaveChangesAsync(cancellationToken);

        dto.CotizacionId = existente.CotizacionId;
        return dto;
    }
}
