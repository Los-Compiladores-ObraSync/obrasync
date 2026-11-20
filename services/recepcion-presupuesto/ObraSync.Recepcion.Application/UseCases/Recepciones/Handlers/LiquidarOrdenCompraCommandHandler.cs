using Microsoft.EntityFrameworkCore;
using ObraSync.Recepcion.Application.Common.Interfaces;
using ObraSync.Recepcion.Application.DTOs;
using ObraSync.Recepcion.Application.UseCases.Recepciones.Commands;
using ObraSync.Recepcion.Domain.Enums;

namespace ObraSync.Recepcion.Application.UseCases.Recepciones.Handlers;

/// <summary>
/// Caso de uso para liquidar administrativamente una orden de compra (#92).
/// Bloquea la liquidación mientras exista saldo pendiente de recepción (criterio CU-06).
/// </summary>
public class LiquidarOrdenCompraCommandHandler
{
    private readonly IRecepcionDbContext _context;

    public LiquidarOrdenCompraCommandHandler(IRecepcionDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OrdenCompraLiquidadaDto> Handle(
        LiquidarOrdenCompraCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.OrdenCompraId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de la orden de compra es obligatorio.", nameof(command.OrdenCompraId));
        }

        if (command.UsuarioId == Guid.Empty)
        {
            throw new ArgumentException("El identificador del usuario es obligatorio.", nameof(command.UsuarioId));
        }

        var ordenCompra = await _context.OrdenesCompra
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken);

        if (ordenCompra is null)
        {
            throw new KeyNotFoundException($"No se encontró la orden de compra {command.OrdenCompraId}.");
        }

        var recibidosPorArticulo = await _context.Recepciones
            .AsNoTracking()
            .Where(r => r.OrdenCompraId == command.OrdenCompraId && r.Estado != EstadoRecepcion.Rechazada)
            .SelectMany(r => r.Detalles)
            .GroupBy(d => d.ArticuloId)
            .Select(g => new { ArticuloId = g.Key, Total = g.Sum(d => d.CantidadRecibida) })
            .ToDictionaryAsync(x => x.ArticuloId, x => x.Total, cancellationToken);

        ordenCompra.Liquidar(command.UsuarioId, recibidosPorArticulo);

        await _context.SaveChangesAsync(cancellationToken);

        return new OrdenCompraLiquidadaDto
        {
            OrdenCompraId = ordenCompra.Id,
            ObraId = ordenCompra.ObraId,
            MontoLiquidado = ordenCompra.MontoTotal,
            LiquidadaPorUsuarioId = ordenCompra.LiquidadaPorUsuarioId!.Value,
            LiquidadaEn = ordenCompra.LiquidadaEn!.Value
        };
    }
}
